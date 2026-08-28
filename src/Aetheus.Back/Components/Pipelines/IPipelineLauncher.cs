// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineLauncher
{
    Task<PipelineRunDto?> TriggerRunAsync(int id, Dictionary<string, string>? additionalVariables = null,
        Dictionary<string, string>? parameters = null, CancellationToken ct = default);
}
