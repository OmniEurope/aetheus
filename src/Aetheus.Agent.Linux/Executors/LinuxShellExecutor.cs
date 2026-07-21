// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Linux.Executors;

public sealed class LinuxShellExecutor(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ILogger<LinuxShellExecutor> logger) : IExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public ExecutorType Type => ExecutorType.Shell;

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

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        // Phase 3 hardening: tighten the file-creation mask (0077 - new files are owner-only, so a
        // step can't leave world/group-readable artifacts in shared dirs) and cap the largest file a
        // step may write (RLIMIT_FSIZE, ~4 GiB) as a cheap disk-fill backstop. Both are best-effort
        // (`|| true`) so a shell that lacks ulimit support still runs. The command is appended as a
        // single -c argument, so this preamble runs in the same shell before the user script.
        psi.ArgumentList.Add("umask 0077\nulimit -f 4194304 2>/dev/null || true\n" + command);

        if (!string.IsNullOrWhiteSpace(_options.WorkDirectory) && Directory.Exists(_options.WorkDirectory))
            psi.WorkingDirectory = _options.WorkDirectory;

        // NOTE: do NOT force DOCKER_BUILDKIT=1 here. Enabling BuildKit requires the buildx CLI plugin to
        // be installed on the agent box; on a box without it (the default VPS-sim, most stock hosts) a
        // `docker build` then fails hard ("BuildKit is enabled but the buildx component is missing"),
        // which is far worse than the cosmetic "legacy builder is deprecated" warning it would silence.
        // To remove that warning, install docker-buildx-plugin in the agent install script instead.
        foreach (var (key, value) in environmentVariables)
            psi.Environment[key] = value;

        // Dev mode: AllowInsecureCerts already disables TLS validation for the agent's own
        // backend connection - git clones in pipeline steps must accept the same self-signed
        // cert or every checkout fails. Never enabled in production config.
        if (_options.AllowInsecureCerts)
            psi.Environment["GIT_SSL_NO_VERIFY"] = "true";

        logger.LogDebug("Executing shell command (timeout={Timeout}s)", timeoutSeconds);

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
            catch (Exception killEx) { logger.LogDebug(killEx, "Best-effort kill after timeout failed"); }
            return new ExecutorResult(-1, true);
        }
    }
}
