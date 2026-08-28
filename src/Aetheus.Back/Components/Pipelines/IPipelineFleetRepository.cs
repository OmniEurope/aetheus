// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

internal interface IPipelineFleetRepository
{
    Task<PaginatedResult<PipelineFleetItemDto>> GetPageAsync(
        PipelineFleetPaginationRequest request,
        IReadOnlyCollection<int>? organizationIds,
        IReadOnlyCollection<int>? accessiblePipelineIds,
        CancellationToken ct);

    Task<PipelineFleetRow?> GetAsync(int pipelineId, CancellationToken ct);
}
