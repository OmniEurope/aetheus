// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Tasks.Events;

/// <summary>
/// A pipeline-owned task reached a terminal state and the run may now advance.
///
/// Tasks used to call the orchestrator directly through a port declared outside both modules. That
/// port kept the compiler happy but not the dependency graph: resolving it still landed on
/// <c>PipelineRunService</c>, so Tasks depended on Pipelines and the two sat in the same cycle.
///
/// The event inverts it. "A step finished" is a notification from the lower layer, and the
/// orchestrator is what reacts to it - which is the direction the module layering wants anyway.
/// It is dispatched <b>strictly</b>: a handler failure propagates to the caller, exactly as the
/// direct call did, so a run cannot be left mid-stage while the task reports success.
/// </summary>
/// <param name="PipelineRunId">Run the completed task belongs to.</param>
/// <param name="StageName">
/// Stage whose step completed, or <c>null</c> when the task carries no step - an artifact collection
/// task, which the run resumes from rather than advances.
/// </param>
/// <param name="TaskStatus">Terminal status the task settled on.</param>
/// <param name="IsArtifactCollection">
/// Whether this was the run's artifact collection task. Stated rather than inferred from a null
/// stage, so a future task type without a step cannot silently take the collection path.
/// </param>
public sealed record PipelineStepTaskCompletedEvent(
    int PipelineRunId,
    string? StageName,
    TaskExecutionStatus TaskStatus,
    bool IsArtifactCollection) : IDomainEvent;
