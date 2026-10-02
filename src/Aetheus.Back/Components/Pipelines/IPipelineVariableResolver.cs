// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineVariableResolver
{
    Task<(Dictionary<string, string> Resolved, List<string> Warnings, HashSet<string> SecretKeys)> ResolveVariablesWithWarningsAsync(
        PipelineYamlDefinition definition, int? projectId, Dictionary<string, string>? additionalVariables, CancellationToken ct,
        int? pipelineId = null, int? runId = null, string? pipelineName = null, int? buildNumber = null,
        bool enforceResolvedReferences = false);
    Task<Dictionary<string, string>> ResolveFullVariablesForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct);
    Task<(Dictionary<string, string> Resolved, HashSet<string> SecretKeys)> ResolveFullVariablesWithSecretsForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct);

    /// <summary>
    /// The warnings still true today. A run records its warnings once, at launch, so a "variable
    /// library not found" written then keeps being shown after the library is created, on a run that is
    /// still going. This re-asks the question for those warnings only and drops the ones that have since
    /// been answered; every other warning is returned untouched, in order.
    /// </summary>
    Task<List<string>> DropResolvedLibraryWarningsAsync(
        IReadOnlyCollection<string> warnings, int? projectId, CancellationToken ct = default);
}
