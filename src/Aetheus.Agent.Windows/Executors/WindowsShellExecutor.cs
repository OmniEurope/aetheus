// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Windows.Executors;

public sealed class WindowsShellExecutor(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ILogger<WindowsShellExecutor> logger) : IExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    private static readonly Lazy<string> CachedShell = new(DetectPowerShell);

    public ExecutorType Type => ExecutorType.Shell;

    private static string DetectPowerShell()
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "-NoLogo -NonInteractive -Command \"exit 0\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                if (p?.ExitCode == 0) return candidate;
            }
            catch { /* not found */ }
        }
        return "powershell";
    }

    public async Task<ExecutorResult> ExecuteAsync(
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (!commandValidator.IsAllowed(command))
        {
            var reason = commandValidator.GetRejectionReason(command) ?? "Unknown";
            logger.LogWarning("Command blocked by validator: {Reason}", reason);
            await onOutput($"Command blocked by security policy - {reason}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (commandValidator.HasDangerousEnvironmentVariables(environmentVariables))
        {
            logger.LogWarning("Dangerous environment variables detected");
            await onOutput("Blocked: dangerous environment variables detected", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        var shell = CachedShell.Value;
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        if (!string.IsNullOrWhiteSpace(_options.WorkDirectory) && Directory.Exists(_options.WorkDirectory))
            psi.WorkingDirectory = _options.WorkDirectory;

        foreach (var (key, value) in environmentVariables)
            psi.Environment[key] = value;

        // Dev mode: AllowInsecureCerts already disables TLS validation for the agent's own
        // backend connection - git clones in pipeline steps must accept the same self-signed
        // cert or every checkout fails. Never enabled in production config.
        if (_options.AllowInsecureCerts)
            psi.Environment["GIT_SSL_NO_VERIFY"] = "true";

        logger.LogDebug("Executing {Shell} command (timeout={Timeout}s)", shell, timeoutSeconds);

        using var process = new Process { StartInfo = psi };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        process.Start();

        var stdoutTask = ExecutorHelper.StreamOutputAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, timeoutCts.Token, logger);
        var stderrTask = ExecutorHelper.StreamOutputAsync(process.StandardError, TaskLogLevel.Error, onOutput, timeoutCts.Token, logger);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new ExecutorResult(process.ExitCode, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Process timed out after {Timeout}s, killing", timeoutSeconds);
            try { process.Kill(entireProcessTree: true); }
            catch (System.ComponentModel.Win32Exception killEx) { logger.LogDebug(killEx, "Best-effort kill after timeout failed (Win32)"); }
            catch (InvalidOperationException killEx) { logger.LogDebug(killEx, "Best-effort kill after timeout failed (process exited)"); }
            return new ExecutorResult(-1, true);
        }
    }
}
