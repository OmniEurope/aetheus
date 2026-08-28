// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Tasks.Events;

/// <summary>
/// A completed task's log carried pipeline telemetry markers, already parsed.
///
/// Tasks owns marker parsing because it owns the log; Pipelines owns persistence because it owns the
/// run. The write port that used to bridge them resolved to <c>PipelineRepository</c>, so it did not
/// actually break the Tasks-to-Pipelines dependency it was introduced to avoid.
///
/// Dispatched strictly: metrics silently lost would make a run page under-report without anything
/// failing, which is the kind of quiet gap a caught-and-logged handler produces.
/// </summary>
/// <param name="PipelineRunId">Run the metrics belong to.</param>
/// <param name="Metrics">Parsed metrics, never empty - the emitter does not raise the event otherwise.</param>
public sealed record PipelineStepTaskMetricsCollectedEvent(
    int PipelineRunId,
    IReadOnlyList<RunMetric> Metrics) : IDomainEvent;
