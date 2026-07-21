// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppErrorRepository(AppDbContext db) : IAppErrorRepository
{
    // S-TECH-EFPR: two ingests of the same NEW fingerprint can both read "not existing" and then both
    // INSERT, violating the unique (MonitoredAppId, Fingerprint) index. On that conflict we re-read (the
    // row now exists) and re-apply as an increment. A few passes cover a realistic concurrency burst.
    private const int MaxUpsertAttempts = 3;

    public async Task<int> UpsertErrorsAsync(int appId, IReadOnlyCollection<AppErrorUpsert> errors, CancellationToken ct = default)
    {
        if (errors.Count == 0) return 0;

        // Collapse the incoming batch by fingerprint first (fewer DB rows to touch). Pure - recomputed
        // identically on every retry pass, so the upsert stays idempotent regardless of how many passes run.
        var grouped = errors
            .GroupBy(e => e.Fingerprint)
            .Select(g => new
            {
                Fingerprint = g.Key,
                Count = g.Count(),
                Last = g.MaxBy(x => x.At),
                First = g.MinBy(x => x.At)
            })
            .ToList();

        var fingerprints = grouped.Select(g => g.Fingerprint).ToList();

        for (var attempt = 1; ; attempt++)
        {
            var existing = await db.AppErrorEvents
                .Where(e => e.MonitoredAppId == appId && fingerprints.Contains(e.Fingerprint))
                .ToDictionaryAsync(e => e.Fingerprint, ct).ConfigureAwait(false);

            foreach (var g in grouped)
            {
                if (existing.TryGetValue(g.Fingerprint, out var row))
                {
                    row.OccurrenceCount += g.Count;
                    if (g.Last.At > row.LastSeenAt) { row.LastSeenAt = g.Last.At; row.Message = g.Last.Message; }
                    if (g.First.At < row.FirstSeenAt) row.FirstSeenAt = g.First.At;
                }
                else
                {
                    db.AppErrorEvents.Add(new AppErrorEvent
                    {
                        MonitoredAppId = appId,
                        Fingerprint = g.Fingerprint,
                        ExceptionType = g.Last.ExceptionType,
                        Message = g.Last.Message,
                        TopFrame = g.Last.TopFrame,
                        OccurrenceCount = g.Count,
                        FirstSeenAt = g.First.At,
                        LastSeenAt = g.Last.At
                    });
                }
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return grouped.Count;
            }
            catch (DbUpdateException ex) when (attempt < MaxUpsertAttempts && IsUniqueViolation(ex))
            {
                // SaveChanges is atomic - nothing persisted. Detach this pass's tracked changes so the
                // retry re-reads a clean slate (the concurrently-inserted row now takes the update branch).
                foreach (var entry in db.ChangeTracker.Entries<AppErrorEvent>().ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>True when the failure is a Postgres unique-constraint violation (SQLSTATE 23505).</summary>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public async Task<(List<AppErrorEvent> Items, int TotalCount)> GetErrorsAsync(int appId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.AppErrorEvents.AsNoTracking().Where(e => e.MonitoredAppId == appId);
        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(e => e.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.AppErrorEvents.Where(e => e.LastSeenAt < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var expired = await db.AppErrorEvents.Where(e => e.LastSeenAt < cutoff).ToListAsync(ct).ConfigureAwait(false);
        db.AppErrorEvents.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
