// SPDX-License-Identifier: EUPL-1.2
using System.Data;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Issues the per-pipeline sequential build number exposed to steps as
/// <c>BUILD_PIPELINE_RUNNUMBER</c>. Split out of <see cref="PipelineCoreRepository"/> because the
/// reservation owns its own transaction and row-locking contract rather than sharing the core
/// repository's plain change-tracker access.
/// </summary>
internal sealed class PipelineBuildNumberRepository(AppDbContext db)
{
    /// <summary>
    /// Reserves the next build number for <paramref name="pipelineId"/>, or 0 when that pipeline no
    /// longer exists. The increment and the read-back run inside one transaction, opened here or
    /// inherited from the caller: the UPDATE takes the provider row lock on the pipeline and holds it
    /// until that transaction commits, so a concurrent launch of the same pipeline blocks there
    /// instead of reading the same value. Read Committed is sufficient because every contender takes
    /// that same lock before reading.
    /// </summary>
    public async Task<int> ReserveNextAsync(int pipelineId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            // InMemory (unit tests): no bulk update and no row locks, so increment the tracked entity.
            var tracked = await db.Pipelines
                .FirstOrDefaultAsync(pipeline => pipeline.Id == pipelineId, ct).ConfigureAwait(false);
            if (tracked is null) return 0;
            tracked.BuildCounter++;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return tracked.BuildCounter;
        }

        // Join an ambient transaction rather than opening a nested one: the webhook path already runs
        // inside IDbTransactionScope.ExecuteInTransactionAsync, and a second BeginTransactionAsync on
        // the same connection throws. Reusing it keeps the row lock, since the caller's transaction
        // holds it just as well and commits it. Only commit what we opened ourselves.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false)
            : null;

        var rows = await db.Pipelines
            .Where(pipeline => pipeline.Id == pipelineId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(pipeline => pipeline.BuildCounter, pipeline => pipeline.BuildCounter + 1),
                ct)
            .ConfigureAwait(false);
        if (rows == 0)
        {
            if (transaction is not null) await transaction.CommitAsync(ct).ConfigureAwait(false);
            return 0;
        }

        var reserved = await db.Pipelines
            .AsNoTracking()
            .Where(pipeline => pipeline.Id == pipelineId)
            .Select(pipeline => pipeline.BuildCounter)
            .SingleAsync(ct).ConfigureAwait(false);
        if (transaction is not null) await transaction.CommitAsync(ct).ConfigureAwait(false);

        // ExecuteUpdateAsync bypasses the change tracker. Evict any tracked Pipeline so a later
        // SaveChangesAsync in the same scope cannot write back a stale BuildCounter.
        var trackedPipeline = db.ChangeTracker.Entries<Pipeline>()
            .FirstOrDefault(entry => entry.Entity.Id == pipelineId);
        if (trackedPipeline is not null)
            trackedPipeline.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

        return reserved;
    }
}
