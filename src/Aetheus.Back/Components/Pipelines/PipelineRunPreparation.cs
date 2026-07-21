// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Immutable run input resolved before authorization. The controller and non-interactive launch paths
/// authorize <see cref="TargetServerIds"/>, then the runner executes this exact YAML and commit snapshot.
/// </summary>
public sealed record PipelineRunPreparation
{
    public required int PipelineId { get; init; }
    public string? BranchName { get; init; }
    public string? CommitHash { get; init; }
    public required string YamlSnapshot { get; init; }
    public required IReadOnlyCollection<int> TargetServerIds { get; init; }
    internal Pipeline Pipeline { get; init; } = default!;
    internal PipelineYamlDefinition Definition { get; init; } = default!;
    internal List<PipelineStageDefinition> EffectiveStages { get; init; } = [];
    internal int? EffectiveProjectId { get; init; }
}
