// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.DTOs;

/// <summary>Lightweight live queue snapshot used by the run page without reloading the run graph.</summary>
public sealed record PipelineRunQueueStateDto
{
    public int RunId { get; init; }
    public List<PipelineStepQueueStateDto> Steps { get; init; } = [];
}

public sealed record PipelineStepQueueStateDto
{
    public int StepId { get; init; }
    public int TaskId { get; init; }
    public int Position { get; init; }
    public int Depth { get; init; }
}
