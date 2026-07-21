// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text;
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Low-level <c>git</c> process executor shared by <see cref="GitLightCliService"/> (read/admin
/// operations) and <see cref="GitLightCliWriter"/> (content-mutating operations). Extracted to a
/// real collaborator so the two services can share the runner without a <c>partial</c> split.
/// </summary>
public class GitProcessRunner(ILogger<GitProcessRunner> logger)
{
    private static readonly TimeSpan DefaultTimeout = BackendRuntimeDefaults.GitProcessTimeout;

    public async Task<(int ExitCode, string Output, string Error)> RunGitAsync(
        string workDir, IReadOnlyList<string> args, CancellationToken ct,
        TimeSpan? timeout = null, bool ignoreExitCode = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        // Guard a missing working directory before Process.Start: it otherwise throws a raw
        // Win32Exception (267, "the directory name is invalid") that bubbles out as an unhandled 500.
        // A repo registered in the DB but absent on disk (e.g. a fresh worktree whose runtime data/
        // folder was never populated) is a recoverable read miss, not a crash - return a clean
        // non-zero result that the callers already degrade on (empty tree/commits, surfaced error on writes).
        if (!Directory.Exists(workDir))
        {
            logger.LogWarning("git {Args} skipped - working directory does not exist: {Dir}",
                string.Join(' ', args), workDir);
            return (-1, string.Empty, $"Repository directory not found: {workDir}");
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        string output, error;
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            var waitTask = process.WaitForExitAsync(cts.Token);

            await Task.WhenAll(outputTask, errorTask, waitTask).ConfigureAwait(false);
            output = await outputTask.ConfigureAwait(false);
            error = await errorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            SafeKill(process);
            logger.LogWarning("git {Args} timed out in {Dir}", string.Join(' ', args), workDir);
            return (-1, string.Empty, "Timeout");
        }

        if (process.ExitCode != 0 && !ignoreExitCode)
        {
            logger.LogDebug("git {Args} exited {Code} in {Dir}: {Error}",
                string.Join(' ', args), process.ExitCode, workDir, error);
        }

        return (process.ExitCode, output, error);
    }

    public static void ClearReadOnlyAttributes(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }

    private void SafeKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) { logger.LogWarning(ex, "[GitLight] Kill failed"); }
    }
}
