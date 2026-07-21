// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Pages.Pipelines;

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
}
