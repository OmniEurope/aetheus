// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// PLAN-003 lot 20 / D26: how long each stage and step usually takes, measured on the last
/// successful runs of the same pipeline that started before the run being viewed. Failed, partial
/// and cancelled runs are left out: a run that stopped halfway would pull the average down and make
/// every healthy run look slow.
/// </summary>
public sealed record RunStageBaselinesDto
{
    /// <summary>How many successful runs the figures are drawn from (at most the server's cap).</summary>
    public int SampleRuns { get; init; }

    public List<StageBaselineDto> Stages { get; init; } = [];
    public List<StepBaselineDto> Steps { get; init; } = [];
}

public sealed record StageBaselineDto
{
    public string StageName { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public double AverageSeconds { get; init; }

    /// <summary>The stage's duration in the most recent of the sampled runs.</summary>
    public double LastSeconds { get; init; }

    /// <summary>How many of the sampled runs actually ran this stage.</summary>
    public int Samples { get; init; }
}

public sealed record StepBaselineDto
{
    public string StageName { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string? MatrixLeg { get; init; }
    public bool IsSystem { get; init; }
    public double AverageSeconds { get; init; }
    public double LastSeconds { get; init; }
    public int Samples { get; init; }
}
