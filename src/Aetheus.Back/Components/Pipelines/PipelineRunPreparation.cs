// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

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
    public string? RepositoryUrl { get; init; }

    /// <summary>The revision the definition was read at, when the workspace comes from another
    /// repository (<c>source:</c> block); null when both are one and <see cref="CommitHash"/> says it.</summary>
    public string? DefinitionCommitHash { get; init; }

    /// <summary>The branch the definition was read on, under the same condition.</summary>
    public string? DefinitionBranchName { get; init; }
    public required string YamlSnapshot { get; init; }
    public required IReadOnlyCollection<int> TargetServerIds { get; init; }
    internal Pipeline Pipeline { get; init; } = default!;
    internal PipelineYamlDefinition Definition { get; init; } = default!;
    internal List<PipelineStageDefinition> EffectiveStages { get; init; } = [];
    internal int? EffectiveProjectId { get; init; }

    /// <summary>Keys of the definition this backend does not know and skipped (recette R2-041), copied
    /// into the run's warnings so the skip is visible where the run is read.</summary>
    internal IReadOnlyList<string> DefinitionWarnings { get; init; } = [];
}
