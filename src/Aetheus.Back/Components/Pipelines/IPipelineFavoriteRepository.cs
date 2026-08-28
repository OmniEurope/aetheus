// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineFavoriteRepository
{
    Task<List<int>> GetPipelineIdsAsync(
        string username,
        List<int>? accessibleIds = null,
        CancellationToken ct = default);

    Task<PipelineFavoriteDto?> SetFavoriteAsync(
        string username,
        int pipelineId,
        bool isFavorite,
        CancellationToken ct = default);
}
