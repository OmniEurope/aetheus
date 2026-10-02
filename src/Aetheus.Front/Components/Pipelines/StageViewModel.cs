// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// View model for one pipeline stage on the run view (a stage groups its step runs). Promoted from a
/// private nested type to a top-level type so <see cref="PipelineRunTimelineBuilder"/> and the
/// <c>PipelineRun</c> code-behind can share it without a <c>partial</c> split.
/// </summary>
internal sealed class StageViewModel
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<PipelineStepRunDto> Steps { get; init; } = [];
    public TaskExecutionStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool IsSystem { get; init; }
    public string? GroupName { get; init; }

    /// <summary>Depth of this stage in the run's `depends_on` graph. Stages sharing a depth ran
    /// concurrently; null when the run carries no usable definition snapshot.</summary>
    public int? Depth { get; init; }

    /// <summary>The condition that kept this stage from running, when every one of its steps was
    /// cancelled by an unmet condition. A stage in that state used to read as "pending" for the rest
    /// of the run, because the aggregate status had no case for it and fell through to Pending.</summary>
    public string? SkippedCondition { get; init; }
}
