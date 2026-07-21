// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppMonitoringRepository(AppDbContext db) : IAppMonitoringRepository
{
    public async Task<long> GetTelemetryStorageBytesAsync(CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return 0;

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(pg_total_relation_size(quote_ident(schemaname) || '.' || quote_ident(tablename))), 0)::bigint
            FROM pg_tables
            WHERE schemaname = current_schema()
              AND tablename IN ('AppMetricSamples', 'AppMetricHourly', 'AppLogEntries', 'AppErrorEvents', 'AppHealthSamples', 'AppHealthHourly')
            """;
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is long bytes ? bytes : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<List<MonitoredApp>> GetAppsByProjectAsync(int projectId, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Include(a => a.Environment)
            .Include(a => a.Server)
            .OrderBy(a => a.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<MonitoredApp?> GetAppAsync(int id, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .AsNoTracking()
            .Include(a => a.Environment)
            .Include(a => a.Server)
            .Include(a => a.Project)
            .FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<MonitoredApp?> GetAppForUpdateAsync(int id, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<int?> GetAppProjectIdAsync(int id, CancellationToken ct = default)
    {
        var projectId = await db.MonitoredApps
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => (int?)a.ProjectId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return projectId;
    }

    public async Task<Dictionary<int, int>> GetProjectOrgIdsAsync(IReadOnlyCollection<int> projectIds, CancellationToken ct = default)
    {
        return await db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.OrganizationId })
            .ToDictionaryAsync(x => x.Id, x => x.OrganizationId, ct).ConfigureAwait(false);
    }

    public async Task<List<MonitoredApp>> GetBackendProbedAppsAsync(CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .Where(a => a.Enabled && a.ServerId == null && a.ProbeUrl != null)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<MonitoredApp>> GetAppsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .AsNoTracking()
            .Where(a => a.Enabled && a.ServerId == serverId && a.ProbeUrl != null)
            .OrderBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, int?>> GetAppServerIdsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .AsNoTracking()
            .Where(a => appIds.Contains(a.Id))
            .Select(a => new { a.Id, a.ServerId })
            .ToDictionaryAsync(x => x.Id, x => x.ServerId, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, MonitoredApp>> GetAppsByIdsForUpdateAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .Where(a => appIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
    }

    public async Task<List<MonitoredApp>> GetAppsForSummaryAsync(List<int>? accessibleProjectIds, CancellationToken ct = default)
    {
        var query = db.MonitoredApps.AsNoTracking().Where(a => a.Enabled);
        if (accessibleProjectIds is not null)
            query = query.Where(a => accessibleProjectIds.Contains(a.ProjectId));
        return await query
            .Include(a => a.Project)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddAppAsync(MonitoredApp app, CancellationToken ct = default)
    {
        db.MonitoredApps.Add(app);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAppAsync(MonitoredApp app, CancellationToken ct = default)
    {
        db.MonitoredApps.Remove(app);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddSamplesAsync(IEnumerable<AppHealthSample> samples, CancellationToken ct = default)
    {
        db.AppHealthSamples.AddRange(samples);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AppHealthSample>> GetSamplesSinceAsync(int appId, DateTime since, CancellationToken ct = default)
    {
        return await db.AppHealthSamples
            .AsNoTracking()
            .Where(s => s.MonitoredAppId == appId && s.Timestamp >= since)
            .OrderBy(s => s.Timestamp)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<(int Up, int Total)> GetRawUptimeAsync(int appId, DateTime since, CancellationToken ct = default)
    {
        var rows = await db.AppHealthSamples
            .AsNoTracking()
            .Where(s => s.MonitoredAppId == appId && s.Timestamp >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Up = g.Count(s => s.IsUp), Total = g.Count() })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return rows is null ? (0, 0) : (rows.Up, rows.Total);
    }

    public async Task<(int Up, int Total)> GetHourlyUptimeAsync(int appId, DateTime since, CancellationToken ct = default)
    {
        var rows = await db.AppHealthHourly
            .AsNoTracking()
            .Where(h => h.MonitoredAppId == appId && h.HourUtc >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Up = g.Sum(h => h.UpCount), Total = g.Sum(h => h.SampleCount) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return rows is null ? (0, 0) : (rows.Up, rows.Total);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<MonitoredApp?> GetDeployTargetAppAsync(int projectId, int? environmentId, CancellationToken ct = default)
    {
        var apps = await db.MonitoredApps
            .Where(a => a.Enabled && a.ProjectId == projectId
                && (a.EnvironmentId == environmentId || a.EnvironmentId == null))
            .ToListAsync(ct).ConfigureAwait(false);
        // Prefer an exact environment match, then a project-level (null environment) app.
        return apps.FirstOrDefault(a => a.EnvironmentId == environmentId && environmentId != null)
            ?? apps.FirstOrDefault(a => a.EnvironmentId == null);
    }

    public async Task<int?> GetAppIdByIngestKeyHashAsync(string ingestKeyHash, CancellationToken ct = default)
    {
        return await db.MonitoredApps
            .AsNoTracking()
            .Where(a => a.Enabled && a.IngestKeyHash == ingestKeyHash)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task TouchIngestAsync(int appId, long droppedDelta, DateTime now, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.MonitoredApps
                .Where(a => a.Id == appId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.LastIngestAt, now)
                    .SetProperty(a => a.IngestDroppedCount, a => a.IngestDroppedCount + droppedDelta), ct)
                .ConfigureAwait(false);
            return;
        }

        // InMemory fallback - ExecuteUpdate not supported
        var app = await db.MonitoredApps.FirstOrDefaultAsync(a => a.Id == appId, ct).ConfigureAwait(false);
        if (app is null) return;
        app.LastIngestAt = now;
        app.IngestDroppedCount += droppedDelta;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> AggregateRawIntoHourlyAsync(DateTime currentHourStartUtc, DateTime rawFloorUtc, CancellationToken ct = default)
    {
        // Load the raw samples in the retention window that belong to completed hours, plus the hours
        // already aggregated, then insert one AppHealthHourly row per (app, hour) bucket not yet present.
        var raw = await db.AppHealthSamples
            .AsNoTracking()
            .Where(s => s.Timestamp >= rawFloorUtc && s.Timestamp < currentHourStartUtc)
            .Select(s => new { s.MonitoredAppId, s.Timestamp, s.IsUp, s.ResponseTimeMs })
            .ToListAsync(ct).ConfigureAwait(false);

        if (raw.Count == 0)
            return 0;

        var existing = await db.AppHealthHourly
            .AsNoTracking()
            .Where(h => h.HourUtc >= rawFloorUtc && h.HourUtc < currentHourStartUtc)
            .Select(h => new { h.MonitoredAppId, h.HourUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        var existingKeys = existing.Select(e => (e.MonitoredAppId, e.HourUtc)).ToHashSet();

        var buckets = raw
            .GroupBy(s => (s.MonitoredAppId, Hour: TruncateToHour(s.Timestamp)))
            .Where(g => !existingKeys.Contains((g.Key.MonitoredAppId, g.Key.Hour)));

        var toInsert = new List<AppHealthHourly>();
        foreach (var bucket in buckets)
        {
            var responseTimes = bucket.Where(s => s.ResponseTimeMs.HasValue).Select(s => (double)s.ResponseTimeMs!.Value).ToList();
            toInsert.Add(new AppHealthHourly
            {
                MonitoredAppId = bucket.Key.MonitoredAppId,
                HourUtc = bucket.Key.Hour,
                SampleCount = bucket.Count(),
                UpCount = bucket.Count(s => s.IsUp),
                AvgResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Average() : 0,
                P95ResponseTimeMs = Percentile(responseTimes, 0.95)
            });
        }

        if (toInsert.Count == 0)
            return 0;

        db.AppHealthHourly.AddRange(toInsert);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toInsert.Count;
    }

    public async Task<int> PurgeRawOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            return await db.AppHealthSamples
                .Where(s => s.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        // InMemory fallback - ExecuteDelete not supported
        var expired = await db.AppHealthSamples
            .Where(s => s.Timestamp < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
        db.AppHealthSamples.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }

    public async Task<int> PurgeHourlyOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            return await db.AppHealthHourly
                .Where(h => h.HourUtc < cutoff)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        // InMemory fallback - ExecuteDelete not supported
        var expired = await db.AppHealthHourly
            .Where(h => h.HourUtc < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
        db.AppHealthHourly.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }

    private static DateTime TruncateToHour(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        if (values.Count == 1)
            return values[0];
        var rank = percentile * (values.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
            return values[low];
        var weight = rank - low;
        return values[low] * (1 - weight) + values[high] * weight;
    }
}
