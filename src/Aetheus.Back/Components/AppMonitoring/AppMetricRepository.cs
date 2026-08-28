// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppMetricRepository(AppDbContext db) : IAppMetricRepository
{
    public async Task AddSamplesAsync(IEnumerable<AppMetricSample> samples, CancellationToken ct = default)
    {
        db.AppMetricSamples.AddRange(samples);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<string>> GetMetricNamesAsync(int appId, CancellationToken ct = default)
    {
        return await db.AppMetricSamples
            .AsNoTracking()
            .Where(m => m.MonitoredAppId == appId)
            .Select(m => m.MetricName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountDistinctMetricNamesAsync(int appId, CancellationToken ct = default)
    {
        return await db.AppMetricSamples
            .AsNoTracking()
            .Where(m => m.MonitoredAppId == appId)
            .Select(m => m.MetricName)
            .Distinct()
            .CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AppMetricSample>> GetRawSeriesAsync(int appId, string metricName, DateTime since, CancellationToken ct = default)
    {
        return await db.AppMetricSamples
            .AsNoTracking()
            .Where(m => m.MonitoredAppId == appId && m.MetricName == metricName && m.Timestamp >= since)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AppMetricHourly>> GetHourlySeriesAsync(int appId, string metricName, DateTime since, CancellationToken ct = default)
    {
        return await db.AppMetricHourly
            .AsNoTracking()
            .Where(m => m.MonitoredAppId == appId && m.MetricName == metricName && m.HourUtc >= since)
            .OrderBy(m => m.HourUtc)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AppMetricThreshold>> GetThresholdsForAppAsync(int appId, CancellationToken ct = default)
    {
        return await db.AppMetricThresholds
            .AsNoTracking()
            .Where(t => t.MonitoredAppId == appId)
            .OrderBy(t => t.MetricName)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AppMetricThreshold>> GetEnabledThresholdsForAppAsync(int appId, CancellationToken ct = default)
    {
        // Tracked (not AsNoTracking): the ingest path mutates IsBreached/LastTriggeredAt and saves.
        return await db.AppMetricThresholds
            .Where(t => t.MonitoredAppId == appId && t.Enabled)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<AppMetricThreshold?> GetThresholdForUpdateAsync(int id, CancellationToken ct = default)
    {
        return await db.AppMetricThresholds.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<int?> GetThresholdAppIdAsync(int id, CancellationToken ct = default)
    {
        return await db.AppMetricThresholds
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => (int?)t.MonitoredAppId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task AddThresholdAsync(AppMetricThreshold threshold, CancellationToken ct = default)
    {
        db.AppMetricThresholds.Add(threshold);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveThresholdAsync(AppMetricThreshold threshold, CancellationToken ct = default)
    {
        db.AppMetricThresholds.Remove(threshold);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> AggregateRawIntoHourlyAsync(DateTime currentHourStartUtc, DateTime rawFloorUtc, CancellationToken ct = default)
    {
        var raw = await db.AppMetricSamples
            .AsNoTracking()
            .Where(m => m.Timestamp >= rawFloorUtc && m.Timestamp < currentHourStartUtc)
            .Select(m => new { m.MonitoredAppId, m.MetricName, m.Timestamp, m.Value })
            .ToListAsync(ct).ConfigureAwait(false);
        if (raw.Count == 0) return 0;

        var existing = await db.AppMetricHourly
            .AsNoTracking()
            .Where(h => h.HourUtc >= rawFloorUtc && h.HourUtc < currentHourStartUtc)
            .Select(h => new { h.MonitoredAppId, h.MetricName, h.HourUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        var existingKeys = existing.Select(e => (e.MonitoredAppId, e.MetricName, e.HourUtc)).ToHashSet();

        var toInsert = new List<AppMetricHourly>();
        foreach (var bucket in raw
                     .GroupBy(m => (m.MonitoredAppId, m.MetricName, Hour: TelemetryMath.TruncateToHour(m.Timestamp)))
                     .Where(g => !existingKeys.Contains((g.Key.MonitoredAppId, g.Key.MetricName, g.Key.Hour))))
        {
            var values = bucket.Select(b => b.Value).ToList();
            toInsert.Add(new AppMetricHourly
            {
                MonitoredAppId = bucket.Key.MonitoredAppId,
                MetricName = bucket.Key.MetricName,
                HourUtc = bucket.Key.Hour,
                SampleCount = values.Count,
                MinValue = values.Min(),
                MaxValue = values.Max(),
                AvgValue = values.Average(),
                P95Value = TelemetryMath.Percentile(values, 0.95)
            });
        }

        if (toInsert.Count == 0) return 0;
        db.AppMetricHourly.AddRange(toInsert);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toInsert.Count;
    }

    public async Task<int> PurgeRawOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.AppMetricSamples.Where(m => m.Timestamp < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var expired = await db.AppMetricSamples.Where(m => m.Timestamp < cutoff).ToListAsync(ct).ConfigureAwait(false);
        db.AppMetricSamples.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }

    public async Task<int> PurgeHourlyOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.AppMetricHourly.Where(h => h.HourUtc < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var expired = await db.AppMetricHourly.Where(h => h.HourUtc < cutoff).ToListAsync(ct).ConfigureAwait(false);
        db.AppMetricHourly.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
