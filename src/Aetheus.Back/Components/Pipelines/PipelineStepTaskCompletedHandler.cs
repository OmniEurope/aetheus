// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Advances the run when a pipeline-owned task settles.
///
/// This is the Pipelines half of the inversion that removed the Tasks-to-Pipelines dependency: Tasks
/// announces that a step finished, and the orchestrator - which is what actually knows what to do
/// next - reacts. The behaviour is identical to the direct call it replaces, including the fact that
/// a failure here reaches the caller: the dispatch is strict.
/// </summary>
internal sealed class PipelineStepTaskCompletedHandler(IPipelineRunService runService)
    : IDomainEventHandler<PipelineStepTaskCompletedEvent>
{
    public async Task HandleAsync(PipelineStepTaskCompletedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.StageName is { Length: > 0 } stageName)
        {
            await runService.AdvanceStageAsync(domainEvent.PipelineRunId, stageName, ct).ConfigureAwait(false);
            return;
        }
        // A task with no step is only meaningful to the run when it is the artifact collection; any
        // other stepless task has nothing to advance, and guessing from the missing stage is how a
        // future task type would silently take this path.
        if (domainEvent.IsArtifactCollection)
        {
            await runService
                .ContinueAfterArtifactCollectionAsync(domainEvent.PipelineRunId, domainEvent.TaskStatus, ct)
                .ConfigureAwait(false);
        }
    }
}
