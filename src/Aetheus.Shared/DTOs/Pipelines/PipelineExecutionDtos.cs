// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record DryRunResultDto
{
    public List<DryRunStageDto> Stages { get; init; } = [];
    public Dictionary<string, string> ResolvedVariables { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record DryRunStageDto
{
    public string StageName { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string? Os { get; init; }
    public List<DryRunStepDto> Steps { get; init; } = [];
}

public sealed record DryRunStepDto
{
    public string StepName { get; init; } = string.Empty;
    public string OriginalCommand { get; init; } = string.Empty;
    public string ResolvedCommand { get; init; } = string.Empty;
}

/// <summary>Result of resolving each stage's target server <em>before</em> launching a run,
/// so the UI can warn when no online agent matches a stage.</summary>
public sealed record PipelinePreflightDto
{
    public List<PreflightStageDto> Stages { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record PreflightStageDto
{
    public string StageName { get; init; } = string.Empty;
    public PreflightTargetKind TargetKind { get; init; }

    /// <summary>The agent name / pool name / environment name the stage targets.</summary>
    public string Target { get; init; } = string.Empty;
    public bool Resolved { get; init; }
    public string? ServerName { get; init; }

    /// <summary>Human-readable reason when <see cref="Resolved"/> is false.</summary>
    public string? Reason { get; init; }
}
