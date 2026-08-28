// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// F-32: typed Docker operations. Builds <c>docker</c> CLI argument lists statically - the
/// only user-controlled input is the container/image identifier, which is validated against
/// <see cref="OperationTargetPatterns.DockerTarget"/>.
/// </summary>
public sealed class DockerOperationExecutor(
    IOptions<AetheusAgentOptions> options,
    ILogger<DockerOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.DockerRestartContainer or
        OperationKind.DockerStartContainer or
        OperationKind.DockerStopContainer or
        OperationKind.DockerPullImage;

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
    {
        var targetFailure = await OperationExecutorFailure.ValidateTargetAsync(
            kind, target, onOutput, () => logger.LogWarning("Rejected docker target with invalid format"),
            "Invalid docker target format").ConfigureAwait(false);
        if (targetFailure is not null) return targetFailure;

        var args = BuildArgs(kind, target);
        if (args is null) return new ExecutorResult(-1, false);

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        return await ProcessRunner.RunAsync(psi, timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
    }

    // Exact `docker` argv per kind (unit-testable without spawning). Only the validated target
    // is variable; the verb is fixed. Null for an unhandled kind.
    internal static string[]? BuildArgs(OperationKind kind, string target) => kind switch
    {
        OperationKind.DockerRestartContainer => ["restart", target],
        OperationKind.DockerStartContainer => ["start", target],
        OperationKind.DockerStopContainer => ["stop", target],
        OperationKind.DockerPullImage => ["pull", target],
        _ => null
    };
}
