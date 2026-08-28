// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

public sealed class PipelineFavoriteRepository(AppDbContext db) : IPipelineFavoriteRepository
{
    public async Task<List<int>> GetPipelineIdsAsync(
        string username,
        List<int>? accessibleIds = null,
        CancellationToken ct = default)
    {
        var query = db.PipelineFavorites
            .AsNoTracking()
            .Where(favorite => favorite.User.Username == username);

        if (accessibleIds is not null)
            query = query.Where(favorite => accessibleIds.Contains(favorite.PipelineId));

        return await query
            .OrderBy(favorite => favorite.PipelineId)
            .Select(favorite => favorite.PipelineId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PipelineFavoriteDto?> SetFavoriteAsync(
        string username,
        int pipelineId,
        bool isFavorite,
        CancellationToken ct = default)
    {
        var userId = await db.Users
            .Where(user => user.Username == username && user.IsActive)
            .Select(user => (int?)user.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (userId is null
            || !await db.Pipelines.AnyAsync(pipeline => pipeline.Id == pipelineId, ct).ConfigureAwait(false))
            return null;

        var favorite = await db.PipelineFavorites
            .FirstOrDefaultAsync(
                item => item.UserId == userId.Value && item.PipelineId == pipelineId,
                ct)
            .ConfigureAwait(false);

        if (isFavorite && favorite is null)
        {
            db.PipelineFavorites.Add(new PipelineFavorite
            {
                UserId = userId.Value,
                PipelineId = pipelineId
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else if (!isFavorite && favorite is not null)
        {
            db.PipelineFavorites.Remove(favorite);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new PipelineFavoriteDto
        {
            PipelineId = pipelineId,
            IsFavorite = isFavorite
        };
    }
}
