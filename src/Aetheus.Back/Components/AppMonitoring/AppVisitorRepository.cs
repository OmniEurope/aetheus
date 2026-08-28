// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Npgsql;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppVisitorRepository(AppDbContext db) : IAppVisitorRepository
{
    public async Task RecordAsync(
        int appId, DateOnly dayUtc, string fingerprintHash, DateTime firstSeenAt, CancellationToken ct = default)
    {
        if (await db.AppVisitorIdentities.AsNoTracking().AnyAsync(
                visitor => visitor.MonitoredAppId == appId
                           && visitor.DayUtc == dayUtc
                           && visitor.FingerprintHash == fingerprintHash, ct).ConfigureAwait(false))
            return;

        var entity = new AppVisitorIdentity
        {
            MonitoredAppId = appId,
            DayUtc = dayUtc,
            FingerprintHash = fingerprintHash,
            FirstSeenAt = firstSeenAt
        };
        db.AppVisitorIdentities.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another backend replica inserted the same daily identity between the read and write.
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<List<AppVisitorDayCount>> GetDailyCountsAsync(
        int appId, DateOnly sinceUtc, CancellationToken ct = default)
    {
        var rows = await db.AppVisitorIdentities
            .AsNoTracking()
            .Where(visitor => visitor.MonitoredAppId == appId && visitor.DayUtc >= sinceUtc)
            .GroupBy(visitor => visitor.DayUtc)
            .Select(group => new { DayUtc = group.Key, UniqueVisitors = group.Count() })
            .OrderBy(point => point.DayUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(point => new AppVisitorDayCount(point.DayUtc, point.UniqueVisitors)).ToList();
    }

    public async Task<int> PurgeOlderThanAsync(DateOnly cutoffUtc, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.AppVisitorIdentities.Where(visitor => visitor.DayUtc < cutoffUtc)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var expired = await db.AppVisitorIdentities.Where(visitor => visitor.DayUtc < cutoffUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        db.AppVisitorIdentities.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
