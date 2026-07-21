// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Completes <c>type: trigger</c> steps that are waiting on a child run. When any pipeline run finishes,
/// this finds the parent step(s) that launched it (linked via <c>PipelineStepRun.TriggeredRunId</c>) and
/// still <see cref="TaskExecutionStatus.Running"/>, mirrors the child's terminal status onto the step
/// (Success ⇒ Success; anything else ⇒ Failed, respecting the step's continue_on_error downstream), then
/// re-enters <c>AdvanceStageAsync</c> so the parent run's stage can progress. This is the wait half of the
/// orchestration primitive - the dispatch half is <c>PipelineRunService.CreateTriggerStepAsync</c>.
/// </summary>
public sealed class PipelineRunCompletedTriggerHandler(
    IPipelineRepository repo,
    IPipelineRunService runService,
    TimeProvider timeProvider,
    ILogger<PipelineRunCompletedTriggerHandler> logger)
    : IDomainEventHandler<PipelineRunCompletedEvent>
{
    public async Task HandleAsync(PipelineRunCompletedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var waitingSteps = await repo.FindStepRunsByTriggeredRunIdAsync(domainEvent.PipelineRunId, ct).ConfigureAwait(false);
        var stepStatus = domainEvent.Status == PipelineStatus.Success
            ? TaskExecutionStatus.Success
            : TaskExecutionStatus.Failed;

        foreach (var step in waitingSteps)
        {
            if (step.Status != TaskExecutionStatus.Running) continue; // already resolved / not actually waiting

            step.Status = stepStatus;
            step.ExitCode = domainEvent.Status == PipelineStatus.Success ? 0 : 1;
            step.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation(
                "Trigger step {StepId} (run {ParentRun}) resolved {Status} from child run {ChildRun}",
                step.Id, step.PipelineRunId, stepStatus, domainEvent.PipelineRunId);

            // Re-enter the normal advancement path so the parent stage/run can move on.
            await runService.AdvanceStageAsync(step.PipelineRunId, step.StageName, ct).ConfigureAwait(false);
        }
    }
}
