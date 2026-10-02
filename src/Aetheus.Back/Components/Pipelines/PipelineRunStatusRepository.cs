// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

internal sealed class PipelineRunStatusRepository(AppDbContext db, ILogger logger)
{
    public async Task<PipelineStatus> ResolveTerminalStatusAsync(
        int runId,
        PipelineStatus requestedStatus,
        CancellationToken ct)
    {
        if (requestedStatus != PipelineStatus.Failed)
            return requestedStatus;

        try
        {
            var hasFailedStep = await db.PipelineStepRuns
                .AnyAsync(step => step.PipelineRunId == runId
                    && (step.Status == TaskExecutionStatus.Failed
                        || step.Status == TaskExecutionStatus.Timeout), ct)
                .ConfigureAwait(false);
            var hasFailedArtifactCollection = await db.Tasks
                .AnyAsync(task => task.PipelineRunId == runId
                    && task.Operation == OperationKind.PipelineCollectArtifacts
                    && (task.Status == TaskExecutionStatus.Failed
                        || task.Status == TaskExecutionStatus.Timeout
                        || task.Status == TaskExecutionStatus.Cancelled), ct)
                .ConfigureAwait(false);
            var hasSucceededStep = await db.PipelineStepRuns
                .AnyAsync(step => step.PipelineRunId == runId
                    && !step.IsSystem
                    && step.Status == TaskExecutionStatus.Success, ct)
                .ConfigureAwait(false);

            logger.LogDebug(
                "Run {RunId} safety net: hasFailedStep={HasFailedStep} hasFailedArtifactCollection={HasFailedArtifactCollection} hasSucceededStep={HasSucceededStep}",
                runId,
                hasFailedStep,
                hasFailedArtifactCollection,
                hasSucceededStep);

            return !hasFailedStep && !hasFailedArtifactCollection && hasSucceededStep
                ? PipelineStatus.Success
                : requestedStatus;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Safety net query failed for run {RunId}", runId);
            return requestedStatus;
        }
    }
}
