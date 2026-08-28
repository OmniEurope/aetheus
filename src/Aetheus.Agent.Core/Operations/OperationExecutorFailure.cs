// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal static class OperationExecutorFailure
{
    public static async Task<ExecutorResult?> ValidateTargetAsync(
        OperationKind kind,
        string target,
        Func<string, TaskLogLevel, Task> onOutput,
        Action logWarning,
        string message)
    {
        if (OperationTargetValidator.IsValid(kind, target)) return null;
        logWarning();
        await onOutput(message, TaskLogLevel.Error).ConfigureAwait(false);
        return new ExecutorResult(-1, false);
    }
}
