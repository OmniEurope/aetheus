// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.AppLogEntries.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var expired = await db.AppLogEntries.Where(l => l.Timestamp < cutoff).ToListAsync(ct).ConfigureAwait(false);
        db.AppLogEntries.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
