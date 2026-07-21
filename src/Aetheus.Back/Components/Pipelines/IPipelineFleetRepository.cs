// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

internal interface IPipelineFleetRepository
{
    Task<List<PipelineFleetRow>> GetCandidatesAsync(
        IReadOnlyCollection<int>? organizationIds,
        IReadOnlyCollection<int>? accessiblePipelineIds,
        string? search,
        int? projectId,
        CancellationToken ct);

    Task<PipelineFleetRow?> GetAsync(int pipelineId, CancellationToken ct);
}
