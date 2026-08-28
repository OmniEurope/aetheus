// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineTemplateResolver
{
    Task<PipelineTemplateResolution> ResolveAsync(
        string yamlContent,
        int organizationId,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default);
}
