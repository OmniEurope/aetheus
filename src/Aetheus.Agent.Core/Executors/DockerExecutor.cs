// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text.RegularExpressions;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Executors;

public sealed partial class DockerExecutor(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ILogger<DockerExecutor> logger) : IExecutor
{
    private static readonly Regex ContainerIdPattern = ContainerIdRegex();
    private readonly AetheusAgentOptions _options = options.Value;

    public ExecutorType Type => ExecutorType.Docker;

    public async Task<ExecutorResult> ExecuteAsync(
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (!TryParseExecCommand(command, out var containerId, out var innerCommand, out var parseError))
        {
            // A non-empty containerId means the split succeeded but the id failed the format check
            // (vs. a missing space, which yields an empty id) - preserve the original per-case logging.
            if (containerId.Length > 0)
            {
                logger.LogWarning("Invalid container ID format blocked");
            }
            await onOutput(parseError, TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (!commandValidator.IsAllowed(innerCommand))
        {
            var reason = commandValidator.GetRejectionReason(innerCommand) ?? "Unknown";
            logger.LogWarning("Docker command blocked by validator: {Reason}", reason);
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
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("exec");
        foreach (var (key, value) in environmentVariables)
        {
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"{key}={value}");
        }
        psi.ArgumentList.Add(containerId);
        psi.ArgumentList.Add("/bin/sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(innerCommand);

        logger.LogDebug("Docker exec: container={Container} (timeout={Timeout}s)", containerId, timeoutSeconds);

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
            logger.LogWarning("Docker exec timed out after {Timeout}s, killing", timeoutSeconds);
            try { process.Kill(entireProcessTree: true); } catch (Exception killEx) { logger.LogDebug(killEx, "Best-effort kill after timeout failed"); }
            return new ExecutorResult(-1, true);
        }
    }

    /// <summary>
    /// Pure parse of a docker-exec task payload ("&lt;containerId&gt; &lt;inner command&gt;") into its two
    /// parts, validating the container id against <see cref="ContainerIdPattern"/>. Returns false with a
    /// user-facing <paramref name="error"/> when the payload has no space (missing id/command) or the id
    /// is malformed. Extracted so the parsing/validation is unit-testable without spawning docker.
    /// </summary>
    internal static bool TryParseExecCommand(string command, out string containerId, out string innerCommand, out string error)
    {
        containerId = string.Empty;
        innerCommand = string.Empty;
        error = string.Empty;

        var spaceIndex = command.IndexOf(' ');
        if (spaceIndex < 0)
        {
            error = "Invalid docker exec command: missing container ID or command";
            return false;
        }

        containerId = command[..spaceIndex];
        innerCommand = command[(spaceIndex + 1)..];

        if (!ContainerIdPattern.IsMatch(containerId))
        {
            error = "Invalid container ID format";
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-]*$")]
    private static partial Regex ContainerIdRegex();
}
