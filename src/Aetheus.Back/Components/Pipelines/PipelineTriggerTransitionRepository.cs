// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>Owns the atomic Running-to-terminal transition for trigger steps.</summary>
internal sealed class PipelineTriggerTransitionRepository(AppDbContext db)
{
    internal async Task<bool> TryResolveAsync(
        int stepId,
        TaskExecutionStatus status,
        int exitCode,
        string? outputVariablesJson,
        string? failureCode,
        string? failureReason,
        DateTime completedAt,
        CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.PipelineStepRuns
                .Where(step => step.Id == stepId && step.Status == TaskExecutionStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(step => step.Status, status)
                        .SetProperty(step => step.ExitCode, exitCode)
                        .SetProperty(step => step.OutputVariablesJson, outputVariablesJson)
                        .SetProperty(step => step.FailureCode, failureCode)
                        .SetProperty(step => step.FailureReason, failureReason)
                        .SetProperty(step => step.CompletedAt, completedAt),
                    ct)
                .ConfigureAwait(false);
            return updated == 1;
        }

        var tracked = await db.PipelineStepRuns
            .FirstOrDefaultAsync(
                step => step.Id == stepId && step.Status == TaskExecutionStatus.Running,
                ct)
            .ConfigureAwait(false);
        if (tracked is null)
            return false;
        tracked.Status = status;
        tracked.ExitCode = exitCode;
        tracked.OutputVariablesJson = outputVariablesJson;
        tracked.FailureCode = failureCode;
        tracked.FailureReason = failureReason;
        tracked.CompletedAt = completedAt;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
