// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Aetheus.Agent.Core.Services;

public sealed class DefaultShellRunner(ILogger<DefaultShellRunner> logger) : IShellRunner
{
    public async Task<string> RunAsync(string command, CancellationToken ct, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(isWindows ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? AgentRuntimeDefaults.ShellCommandTimeout);

        using var process = Process.Start(psi)!;
        try
        {
            // Drain stdout and stderr concurrently to avoid blocking when stderr buffer fills.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token)).ConfigureAwait(false);
            return await stdoutTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
    }

    public async Task<string> RunWithStdinAsync(string fileName, IReadOnlyList<string> args, string stdin, CancellationToken ct, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // F-25: ArgumentList escapes each argument independently - no shell interpretation.
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? AgentRuntimeDefaults.ShellCommandTimeout);

        using var process = Process.Start(psi)!;
        try
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token)).ConfigureAwait(false);
            return await stdoutTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
    }

    public async Task<ShellExecResult> RunExecAsync(string fileName, IReadOnlyList<string> args, CancellationToken ct, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(args);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // ArgumentList escapes each argument independently - no shell, no interpolation.
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? AgentRuntimeDefaults.ShellCommandTimeout);

        using var process = Process.Start(psi)!;
        string stdout;
        string stderr;
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token)).ConfigureAwait(false);

            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        // M-2: keep the error channel observable (intrusion-detection / tampering signal)
        // instead of discarding it. Debug level - shell probes legitimately fail often.
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            var snippet = stderr.Length > 500 ? stderr[..500] : stderr;
            logger.LogDebug("Exec '{File}' exited {Exit}; stderr: {Stderr}", fileName, process.ExitCode, snippet.Trim());
        }

        return new ShellExecResult(process.ExitCode, stdout, stderr);
    }

    // A cancelled/timed-out run must not orphan the child: a stuck docker/systemctl/tar
    // would otherwise outlive `using process` and accumulate on the host.
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process exited between the check and the kill - nothing left to clean up.
        }
    }
}
