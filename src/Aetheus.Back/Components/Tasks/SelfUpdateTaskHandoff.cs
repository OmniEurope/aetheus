// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Tasks;

internal static class SelfUpdateTaskHandoff
{
    public static async Task<bool?> TryAcknowledgeAsync(
        ITaskRepository repository,
        TimeProvider timeProvider,
        ServerTask task,
        TaskResultDto result,
        CancellationToken cancellationToken)
    {
        // A successful self-update result acknowledges only that the old process handed control to
        // the service manager. AgentUpdateRepository makes the task terminal only after a new agent
        // session confirms the expected version, protocol and capabilities.
        if (task.Operation != OperationKind.AgentSelfUpdate
            || result.Status != TaskExecutionStatus.Success)
            return null;

        if (result.AgentSessionId is not null
            && result.AgentSessionFencingToken is { } fencingToken
            && !await repository.HasCurrentAgentLeaseAsync(
                    task.Id,
                    task.ServerId,
                    result.AgentSessionId,
                    fencingToken,
                    cancellationToken)
                .ConfigureAwait(false))
            return false;

        task.ExitCode = result.ExitCode;
        task.EnvironmentVariables = TaskEnvProtection.EmptyEnv;
        task.Logs.Add(new TaskLog
        {
            TaskId = task.Id,
            Task = task,
            Level = TaskLogLevel.Info,
            Message = "Agent update handoff accepted; awaiting confirmation from a new agent session.",
            Timestamp = timeProvider.GetUtcNow().UtcDateTime
        });
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
