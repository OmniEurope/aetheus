// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineFavoriteService
{
    Task<PipelineFavoritesDto> GetFavoritesAsync(
        List<int>? accessibleIds = null,
        CancellationToken ct = default);

    Task<PipelineFavoriteDto?> SetFavoriteAsync(
        int pipelineId,
        bool isFavorite,
        CancellationToken ct = default);
}
