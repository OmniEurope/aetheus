// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineRunIdempotencyWriter
{
    internal static async Task<(PipelineRun Run, bool Created)> GetOrAddAsync(
        AppDbContext db, PipelineRun run, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(run.IdempotencyKey))
        {
            var existing = await FindExistingAsync(db, run, ct).ConfigureAwait(false);
            if (existing is not null) return (existing, false);
        }

        db.PipelineRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return (run, true);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(run.IdempotencyKey))
        {
            db.Entry(run).State = EntityState.Detached;
            var existing = await FindExistingAsync(db, run, ct).ConfigureAwait(false);
            if (existing is not null) return (existing, false);
            throw;
        }
    }

    private static Task<PipelineRun?> FindExistingAsync(
        AppDbContext db, PipelineRun run, CancellationToken ct)
        => db.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(
            item => item.PipelineId == run.PipelineId
                && item.IdempotencyKey == run.IdempotencyKey,
            ct);
}
