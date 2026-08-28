// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppLogRepository(AppDbContext db) : IAppLogRepository
{
    public async Task AddLogsAsync(IEnumerable<AppLogEntry> logs, CancellationToken ct = default)
    {
        db.AppLogEntries.AddRange(logs);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(List<AppLogEntry> Items, int TotalCount)> GetLogsAsync(
        int appId, DateTime since, int? minSeverity, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.AppLogEntries
            .AsNoTracking()
            .Where(l => l.MonitoredAppId == appId && l.Timestamp >= since);

        if (minSeverity is { } min)
            query = query.Where(l => l.SeverityNumber >= min);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(l => l.Body.Contains(search));

        return await LoadPageAsync(query, page, pageSize, ct).ConfigureAwait(false);
    }

    private static async Task<(List<AppLogEntry> Items, int TotalCount)> LoadPageAsync(
        IQueryable<AppLogEntry> query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByDescending(log => log.Timestamp)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return db.Database.IsRelational()
            ? await db.AppLogEntries.Where(log => log.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false)
            : await PurgeTrackedAsync(cutoff, ct).ConfigureAwait(false);
    }

    private async Task<int> PurgeTrackedAsync(DateTime cutoff, CancellationToken ct)
    {
        var expired = await db.AppLogEntries.Where(log => log.Timestamp < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
        db.RemoveRange(expired);
        _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
