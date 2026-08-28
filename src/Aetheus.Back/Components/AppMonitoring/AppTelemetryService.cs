// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppTelemetryService(
    IAppMonitoringRepository appRepo,
    IAppMetricRepository metricRepo,
    IAppLogRepository logRepo,
    IAppErrorRepository errorRepo,
    IAppVisitorRepository visitorRepo,
    IngestKeyHasher hasher,
    IIngestService ingestService,
    IAuditService audit,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAppTelemetryService
{
    public async Task<IngestKeyResponse?> GenerateIngestKeyAsync(int appId, CancellationToken ct = default)
    {
        var app = await appRepo.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
        if (app is null)
            return null;

        var plaintext = hasher.Generate();
        var (previousHash, newHash, now) = IngestKeyRotation.Apply(
            app, plaintext, hasher, configuration, timeProvider);
        await appRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        // Evict the rotated-out key so it stops resolving at once, and the new key's hash in case a probe
        // negatively cached it (miss) just before it was assigned - otherwise the new key would 401 for the TTL.
        if (previousHash is not null)
            ingestService.InvalidateKeyCache(previousHash);
        ingestService.InvalidateKeyCache(newHash);
        await audit.LogAsync(
            previousHash is null ? "CreatedIngestKey" : "RotatedIngestKey",
            "MonitoredApp",
            app.Id,
            $"Version {app.IngestKeyVersion}; previous valid until {app.PreviousIngestKeyValidUntil:O}",
            ct).ConfigureAwait(false);
        return new IngestKeyResponse { Key = plaintext, CreatedAt = now };
    }

    public async Task<bool> RevokeIngestKeyAsync(int appId, CancellationToken ct = default)
    {
        var app = await appRepo.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
        if (app is null)
            return false;

        var revokedHash = app.IngestKeyHash;
        var previousHash = app.PreviousIngestKeyHash;
        app.IngestKeyHash = null;
        app.IngestKeyCreatedAt = null;
        app.IngestKeyExpiresAt = null;
        app.PreviousIngestKeyHash = null;
        app.PreviousIngestKeyValidUntil = null;
        await appRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        // A revoked key must stop ingesting immediately, not linger for the resolver cache TTL.
        if (revokedHash is not null)
            ingestService.InvalidateKeyCache(revokedHash);
        if (previousHash is not null)
            ingestService.InvalidateKeyCache(previousHash);
        await audit.LogAsync(
            "RevokedIngestKey",
            "MonitoredApp",
            app.Id,
            $"Version {app.IngestKeyVersion}",
            ct).ConfigureAwait(false);
        return true;
    }

    public Task<List<string>> GetMetricNamesAsync(int appId, CancellationToken ct = default) =>
        metricRepo.GetMetricNamesAsync(appId, ct);

    public async Task<MetricSeriesDto> GetMetricSeriesAsync(int appId, string metricName, int hours, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, AppMonitoringDefaults.MinimumHistoryHours, AppMonitoringDefaults.MaximumMetricHistoryHours);
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-hours);

        // <= 24h: raw resolution (decimated to a bounded point count server-side).
        if (hours <= 24)
        {
            var raw = await metricRepo.GetRawSeriesAsync(appId, metricName, since, ct).ConfigureAwait(false);
            return new MetricSeriesDto
            {
                MetricName = metricName,
                Unit = raw.Count > 0 ? raw[^1].Unit : null,
                Points = Decimate(raw.Select(s => new MetricPointDto { Timestamp = s.Timestamp, Value = s.Value }).ToList())
            };
        }

        // Longer windows: hourly rollup (avg/min/max/p95). The rollup lags - the current incomplete hour and
        // any complete-but-not-yet-swept hours are absent - so append the recent raw tail past the last rolled-up
        // hour, otherwise the most recent points silently vanish from 7d/90d charts.
        var hourly = await metricRepo.GetHourlySeriesAsync(appId, metricName, since, ct).ConfigureAwait(false);
        var points = hourly.Select(h => new MetricPointDto
        {
            Timestamp = h.HourUtc,
            Value = h.AvgValue,
            Min = h.MinValue,
            Max = h.MaxValue,
            P95 = h.P95Value
        }).ToList();

        var tailSince = points.Count > 0 ? points[^1].Timestamp.AddHours(1) : since;
        var tailRaw = await metricRepo.GetRawSeriesAsync(appId, metricName, tailSince, ct).ConfigureAwait(false);
        points.AddRange(tailRaw.Select(s => new MetricPointDto { Timestamp = s.Timestamp, Value = s.Value }));

        return new MetricSeriesDto
        {
            MetricName = metricName,
            Unit = tailRaw.Count > 0 ? tailRaw[^1].Unit : null,
            Points = Decimate(points)
        };
    }

    public async Task<AppVisitorSeriesDto> GetVisitorSeriesAsync(
        int appId, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, AppMonitoringDefaults.MaximumVisitorHistoryDays);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var since = today.AddDays(-(days - 1));
        var counts = await visitorRepo.GetDailyCountsAsync(appId, since, ct).ConfigureAwait(false);
        var byDay = counts.ToDictionary(point => point.DayUtc, point => point.UniqueVisitors);
        var points = Enumerable.Range(0, days)
            .Select(offset => since.AddDays(offset))
            .Select(day => new AppVisitorPointDto
            {
                DayUtc = day,
                UniqueVisitors = byDay.GetValueOrDefault(day)
            })
            .ToList();

        return new AppVisitorSeriesDto
        {
            Today = byDay.GetValueOrDefault(today),
            Points = points
        };
    }

    // Evenly downsample to the shared series limit, preserving chronological order and the last point.
    private static List<MetricPointDto> Decimate(List<MetricPointDto> points)
    {
        if (points.Count <= AppMonitoringDefaults.MaximumSeriesPoints) return points;
        var step = (double)points.Count / AppMonitoringDefaults.MaximumSeriesPoints;
        var result = new List<MetricPointDto>(AppMonitoringDefaults.MaximumSeriesPoints);
        for (var i = 0; i < AppMonitoringDefaults.MaximumSeriesPoints; i++)
            result.Add(points[(int)(i * step)]);
        if (result[^1].Timestamp != points[^1].Timestamp)
            result[^1] = points[^1];
        return result;
    }

    public async Task<List<AppMetricThresholdDto>> GetThresholdsAsync(int appId, CancellationToken ct = default)
    {
        var thresholds = await metricRepo.GetThresholdsForAppAsync(appId, ct).ConfigureAwait(false);
        return thresholds.Select(MapThreshold).ToList();
    }

    public async Task<AppMetricThresholdDto> CreateThresholdAsync(int appId, CreateMetricThresholdRequest request, CancellationToken ct = default)
    {
        var threshold = new AppMetricThreshold
        {
            MonitoredAppId = appId,
            MetricName = request.MetricName.Trim(),
            Operator = request.Operator,
            Threshold = request.Threshold,
            Enabled = request.Enabled
        };
        await metricRepo.AddThresholdAsync(threshold, ct).ConfigureAwait(false);
        return MapThreshold(threshold);
    }

    public Task<int?> GetThresholdAppIdAsync(int thresholdId, CancellationToken ct = default) =>
        metricRepo.GetThresholdAppIdAsync(thresholdId, ct);

    public async Task<bool> DeleteThresholdAsync(int thresholdId, CancellationToken ct = default)
    {
        var threshold = await metricRepo.GetThresholdForUpdateAsync(thresholdId, ct).ConfigureAwait(false);
        if (threshold is null)
            return false;
        await metricRepo.RemoveThresholdAsync(threshold, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PaginatedResult<AppLogEntryDto>> GetLogsAsync(
        int appId, int hours, int? minSeverity, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = PaginationDefaults.Clamp(pageSize);
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-Math.Clamp(hours, AppMonitoringDefaults.MinimumHistoryHours, AppMonitoringDefaults.MaximumHistoryHours));

        var (items, total) = await logRepo.GetLogsAsync(appId, since, minSeverity, search, page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<AppLogEntryDto>
        {
            Items = items.Select(l => new AppLogEntryDto
            {
                Timestamp = l.Timestamp,
                SeverityNumber = l.SeverityNumber,
                SeverityText = l.SeverityText,
                Body = l.Body,
                AttributesJson = l.AttributesJson
            }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaginatedResult<AppErrorEventDto>> GetErrorsAsync(int appId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = PaginationDefaults.Clamp(pageSize);
        var (items, total) = await errorRepo.GetErrorsAsync(appId, page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<AppErrorEventDto>
        {
            Items = items.Select(e => new AppErrorEventDto
            {
                Id = e.Id,
                Fingerprint = e.Fingerprint,
                ExceptionType = e.ExceptionType,
                Message = e.Message,
                TopFrame = e.TopFrame,
                OccurrenceCount = e.OccurrenceCount,
                FirstSeenAt = e.FirstSeenAt,
                LastSeenAt = e.LastSeenAt
            }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static AppMetricThresholdDto MapThreshold(AppMetricThreshold t) => new()
    {
        Id = t.Id,
        MonitoredAppId = t.MonitoredAppId,
        MetricName = t.MetricName,
        Operator = t.Operator,
        Threshold = t.Threshold,
        Enabled = t.Enabled,
        IsBreached = t.IsBreached,
        LastTriggeredAt = t.LastTriggeredAt
    };
}
