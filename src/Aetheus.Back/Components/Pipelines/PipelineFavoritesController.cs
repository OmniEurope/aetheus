// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

[ApiController]
[Route("api/pipelines")]
[Authorize]
public sealed class PipelineFavoritesController(
    IPipelineFavoriteService favoriteService,
    IResourceAuthorizationService authorization) : ControllerBase
{
    [HttpGet("favorites")]
    public async Task<ActionResult<PipelineFavoritesDto>> GetFavorites(CancellationToken ct)
    {
        var accessibleIds = await authorization.GetAccessibleResourceIdsAsync(
            User, ResourceType.Pipeline, Permission.Read, ct);
        if (accessibleIds is { Count: 0 })
            return Ok(new PipelineFavoritesDto());

        return Ok(await favoriteService.GetFavoritesAsync(accessibleIds, ct));
    }

    [HttpPut("{pipelineId:int}/favorite")]
    public async Task<ActionResult<PipelineFavoriteDto>> SetFavorite(
        int pipelineId,
        [FromBody] SetPipelineFavoriteRequest request,
        CancellationToken ct)
    {
        if (!await authorization.HasPermissionAsync(
                User, ResourceType.Pipeline, pipelineId, Permission.Read, ct))
            return Forbid();

        var favorite = await favoriteService.SetFavoriteAsync(pipelineId, request.IsFavorite, ct);
        return favorite is null ? NotFound() : Ok(favorite);
    }
}
