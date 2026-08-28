// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public sealed class PipelineFavoriteService(
    IPipelineFavoriteRepository repository,
    IHttpContextAccessor httpContextAccessor) : IPipelineFavoriteService
{
    public async Task<PipelineFavoritesDto> GetFavoritesAsync(
        List<int>? accessibleIds = null,
        CancellationToken ct = default)
    {
        var username = CurrentUsername();
        if (string.IsNullOrEmpty(username))
            return new PipelineFavoritesDto();

        return new PipelineFavoritesDto
        {
            PipelineIds = await repository
                .GetPipelineIdsAsync(username, accessibleIds, ct)
                .ConfigureAwait(false)
        };
    }

    public Task<PipelineFavoriteDto?> SetFavoriteAsync(
        int pipelineId,
        bool isFavorite,
        CancellationToken ct = default)
    {
        var username = CurrentUsername();
        return string.IsNullOrEmpty(username)
            ? Task.FromResult<PipelineFavoriteDto?>(null)
            : repository.SetFavoriteAsync(username, pipelineId, isFavorite, ct);
    }

    private string? CurrentUsername() =>
        httpContextAccessor.HttpContext?.User.Identity?.Name;
}
