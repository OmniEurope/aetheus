// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineVariableResolver
{
    Task<(Dictionary<string, string> Resolved, List<string> Warnings, HashSet<string> SecretKeys)> ResolveVariablesWithWarningsAsync(
        PipelineYamlDefinition definition, int? projectId, Dictionary<string, string>? additionalVariables, CancellationToken ct,
        int? pipelineId = null, int? runId = null, string? pipelineName = null, int? buildNumber = null);
    Task<Dictionary<string, string>> ResolveFullVariablesForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct);
    Task<(Dictionary<string, string> Resolved, HashSet<string> SecretKeys)> ResolveFullVariablesWithSecretsForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct);
}
