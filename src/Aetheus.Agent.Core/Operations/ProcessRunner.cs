// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// F-32: shared launch/stream/timeout helper for typed operation executors. Centralises the
/// bookkeeping (linked CTS, output streaming, kill-on-timeout) so each operation executor
/// only needs to assemble its <c>ArgumentList</c>.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<ExecutorResult> RunAsync(
        ProcessStartInfo psi,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        ILogger logger,
        CancellationToken cancellationToken,
        string? standardInput = null)
    {
        // When a secret (e.g. a mail admin password) must reach the child process without appearing
        // on the argv / process list, the caller passes it here and we pipe it to stdin, then close.
        if (standardInput is not null)
            psi.RedirectStandardInput = true;

        using var process = new Process { StartInfo = psi };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Hardening (#38): a missing binary (e.g. docker.exe not on PATH, sc.exe denied) used
            // to surface as an unhandled Win32Exception and crash the polling loop. Convert into
            // a structured failure so the task is marked Failed and the agent keeps polling.
            await onOutput($"Failed to launch '{psi.FileName}': {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            logger.LogError(ex, "Process launch failed for {File}", psi.FileName);
            return new ExecutorResult(-1, false);
        }

        if (standardInput is not null)
        {
            try
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child exited before consuming stdin (e.g. `sudo -n` rejected because the grant is
                // not provisioned). Its exit code + stderr are captured below; this broken pipe is a
                // symptom, not the real error - swallow it and let WaitForExit surface the true failure.
            }
        }

        var stdout = ExecutorHelper.StreamOutputAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, timeoutCts.Token, logger);
        // stderr is an output channel, not a semantic failure level: many successful tools use it for
        // progress and diagnostics. Preserve the channel explicitly in the message and reserve Error for
        // the exit-code proof below.
        var stderr = ExecutorHelper.StreamOutputAsync(
            process.StandardError,
            TaskLogLevel.Warning,
            (line, level) => onOutput($"[stderr] {line.TrimEnd()}", level),
            timeoutCts.Token,
            logger);

        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await onOutput(
                $"[process] exit-code={process.ExitCode}",
                process.ExitCode == 0 ? TaskLogLevel.Info : TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(process.ExitCode, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Operation timed out after {Timeout}s, killing", timeoutSeconds);
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            catch (NotSupportedException) { /* not supported on platform */ }
            return new ExecutorResult(-1, true);
        }
    }
}
