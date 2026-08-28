// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// F-32: typed operation pathway. Each <see cref="OperationKind"/> maps to a strict set of
/// arguments built via <c>ProcessStartInfo.ArgumentList</c>. No shell, no string interpolation,
/// no regex allow-list. The target string is validated against a per-kind pattern in
/// <see cref="Aetheus.Shared.Validation.OperationTargetValidator"/> (shared with the backend).
/// </summary>
public interface IOperationExecutor
{
    bool CanHandle(OperationKind kind);

    Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken);

    /// <summary>
    /// Optional overload with caller-supplied environment variables. Used by operations whose
    /// payload exceeds what fits in the target string (item #5.3 carries the base64 vhost
    /// config content in <c>AETHEUS_APACHE_CONFIG_B64</c>). Default implementation discards
    /// env vars and delegates to the simpler overload - existing executors that don't need
    /// env vars need not change.
    /// </summary>
    Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
        => ExecuteAsync(kind, target, timeoutSeconds, onOutput, cancellationToken);
}
