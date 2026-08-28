// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// Hourly sweep (ADR-021 phase 1): aggregates raw <c>AppHealthSample</c> rows from completed hours into
/// <c>AppHealthHourly</c>, then purges raw samples older than the raw-retention window (default 7 days) and
/// hourly aggregates older than the aggregate-retention window (default 90 days). OpenTelemetry signal
/// retention is independent: metric details 30 days, metric aggregates 13 months, logs/traces 14 days.
/// Bounded PG growth per ADR-001.
/// </summary>
public sealed class AppTelemetryRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AppTelemetryRetentionService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly Meter Meter = new("Aetheus.AppMonitoring.Retention", "1.0.0");
    private static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("aetheus.retention.duration", unit: "s");
    private static readonly Counter<long> Deleted =
        Meter.CreateCounter<long>("aetheus.retention.deleted");
    private static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("aetheus.retention.failures");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BackendRuntimeDefaults.MaintenanceInterval);

        // First sweep after startup runs a WIDER aggregation catch-up: if the backend was down longer than
        // the hourly lookback, those completed hours would otherwise never be rolled up (permanent gap in
        // the rollups). Aggregation is idempotent per (app, metric, hour), so a one-time wider pass is safe.
        await SweepSafelyAsync(stoppingToken, catchUp: true).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await SweepSafelyAsync(stoppingToken, catchUp: false).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested)
                break;
        }
    }

    private async Task SweepSafelyAsync(CancellationToken ct, bool catchUp)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var deleted = await SweepAsync(ct, catchUp).ConfigureAwait(false);
            Deleted.Add(deleted);
            Duration.Record(stopwatch.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("result", "success"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Failures.Add(1);
            Duration.Record(stopwatch.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("result", "failure"));
            logger.LogError(ex, "Error during app telemetry retention sweep");
        }
    }

    internal async Task<int> SweepAsync(CancellationToken ct, bool catchUp = false)
    {
        var rawRetentionDays = Math.Clamp(
            configuration.GetValue("AppMonitoring:RawRetentionDays", AppMonitoringDefaults.DefaultRawRetentionDays),
            AppMonitoringDefaults.MinimumRawRetentionDays,
            AppMonitoringDefaults.MaximumRawRetentionDays);
        var hourlyRetentionDays = Math.Clamp(
            configuration.GetValue("AppMonitoring:HourlyRetentionDays", AppMonitoringDefaults.DefaultHourlyRetentionDays),
            AppMonitoringDefaults.MinimumHourlyRetentionDays,
            AppMonitoringDefaults.MaximumHourlyRetentionDays);
        var metricDetailedRetentionDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:MetricDetailedRetentionDays",
                AppMonitoringDefaults.DefaultMetricDetailedRetentionDays),
            AppMonitoringDefaults.MinimumMetricDetailedRetentionDays,
            AppMonitoringDefaults.MaximumMetricDetailedRetentionDays);
        var metricAggregateRetentionMonths = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:MetricAggregateRetentionMonths",
                AppMonitoringDefaults.DefaultMetricAggregateRetentionMonths),
            AppMonitoringDefaults.MinimumMetricAggregateRetentionMonths,
            AppMonitoringDefaults.MaximumMetricAggregateRetentionMonths);
        var logRetentionDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:LogRetentionDays",
                AppMonitoringDefaults.DefaultLogRetentionDays),
            AppMonitoringDefaults.MinimumLogRetentionDays,
            AppMonitoringDefaults.MaximumLogRetentionDays);
        var traceRetentionDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:TraceRetentionDays",
                AppMonitoringDefaults.DefaultTraceRetentionDays),
            AppMonitoringDefaults.MinimumTraceRetentionDays,
            AppMonitoringDefaults.MaximumTraceRetentionDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppMonitoringRepository>();
        var metricRepo = scope.ServiceProvider.GetRequiredService<IAppMetricRepository>();
        var logRepo = scope.ServiceProvider.GetRequiredService<IAppLogRepository>();
        var errorRepo = scope.ServiceProvider.GetRequiredService<IAppErrorRepository>();
        var visitorRepo = scope.ServiceProvider.GetRequiredService<IAppVisitorRepository>();
        var analyticsRepo = scope.ServiceProvider.GetRequiredService<IAppWebAnalyticsRepository>();
        var analyticsConfiguration =
            scope.ServiceProvider.GetRequiredService<IAppWebAnalyticsConfigurationService>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var currentHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        // Aggregation only reads recently-completed hours (bounded), NOT the whole retention window: the
        // sweep runs hourly, so only the last few hours are ever new. Reading 7 d of raw every hour would
        // materialise millions of rows and defeat the bounded-PG-growth goal (audit finding, Critique).
        var aggregationLookbackHours = Math.Clamp(
            configuration.GetValue("AppMonitoring:AggregationLookbackHours", AppMonitoringDefaults.DefaultAggregationLookbackHours),
            AppMonitoringDefaults.MinimumAggregationLookbackHours,
            AppMonitoringDefaults.MaximumAggregationLookbackHours);
        // On the startup catch-up pass, widen the floor to backfill hours missed during a longer downtime
        // (default 48 h, bounded so we never load the whole 7-day raw window into memory at once). Steady-state
        // hourly sweeps keep the short lookback so they stay cheap.
        var startupCatchUpHours = Math.Clamp(
            configuration.GetValue("AppMonitoring:StartupCatchUpHours", AppMonitoringDefaults.DefaultStartupCatchUpHours),
            aggregationLookbackHours,
            AppMonitoringDefaults.MaximumAggregationLookbackHours);
        var effectiveLookbackHours = catchUp ? startupCatchUpHours : aggregationLookbackHours;
        var aggFloor = currentHourStart.AddHours(-effectiveLookbackHours);
        var rawCutoff = now.AddDays(-rawRetentionDays);
        var hourlyCutoff = now.AddDays(-hourlyRetentionDays);
        var metricDetailedCutoff = now.AddDays(-metricDetailedRetentionDays);
        var metricAggregateCutoff = now.AddMonths(-metricAggregateRetentionMonths);
        var logCutoff = now.AddDays(-logRetentionDays);
        var traceCutoff = now.AddDays(-traceRetentionDays);
        var visitorRetentionDays = Math.Clamp(
            configuration.GetValue("AppMonitoring:VisitorRetentionDays", AppMonitoringDefaults.DefaultVisitorRetentionDays),
            AppMonitoringDefaults.MinimumVisitorRetentionDays,
            AppMonitoringDefaults.MaximumVisitorRetentionDays);
        var visitorCutoff = DateOnly.FromDateTime(now).AddDays(-visitorRetentionDays);

        // Availability (phase 1)
        var aggregated = await repo.AggregateRawIntoHourlyAsync(currentHourStart, aggFloor, ct).ConfigureAwait(false);
        var purgedRaw = await repo.PurgeRawOlderThanAsync(rawCutoff, ct).ConfigureAwait(false);
        var purgedHourly = await repo.PurgeHourlyOlderThanAsync(hourlyCutoff, ct).ConfigureAwait(false);

        // Metrics (phase 2): raw -> hourly, purge both
        var metricsAggregated = await metricRepo.AggregateRawIntoHourlyAsync(currentHourStart, aggFloor, ct).ConfigureAwait(false);
        var purgedMetricRaw = await metricRepo.PurgeRawOlderThanAsync(metricDetailedCutoff, ct).ConfigureAwait(false);
        var purgedMetricHourly = await metricRepo.PurgeHourlyOlderThanAsync(metricAggregateCutoff, ct).ConfigureAwait(false);

        // Logs (phase 3): purge only (no aggregate). Trace/error groups use the independent trace window.
        var purgedLogs = await logRepo.PurgeOlderThanAsync(logCutoff, ct).ConfigureAwait(false);
        var purgedErrors = await errorRepo.PurgeOlderThanAsync(traceCutoff, ct).ConfigureAwait(false);
        var purgedVisitors = await visitorRepo.PurgeOlderThanAsync(visitorCutoff, ct).ConfigureAwait(false);
        var analyticsDetailedDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:Analytics:DetailedRetentionDays",
                AppMonitoringDefaults.DefaultAnalyticsDetailedRetentionDays),
            AppMonitoringDefaults.MinimumAnalyticsDetailedRetentionDays,
            AppMonitoringDefaults.MaximumAnalyticsDetailedRetentionDays);
        var analyticsSessionDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:Analytics:SessionRetentionDays",
                AppMonitoringDefaults.DefaultAnalyticsSessionRetentionDays),
            AppMonitoringDefaults.MinimumAnalyticsSessionRetentionDays,
            AppMonitoringDefaults.MaximumAnalyticsSessionRetentionDays);
        var analyticsAggregateMonths = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:Analytics:AggregateRetentionMonths",
                AppMonitoringDefaults.DefaultAnalyticsAggregateRetentionMonths),
            AppMonitoringDefaults.MinimumAnalyticsAggregateRetentionMonths,
            AppMonitoringDefaults.MaximumAnalyticsAggregateRetentionMonths);
        var analyticsRejectionDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:Analytics:RejectionRetentionDays",
                AppMonitoringDefaults.DefaultAnalyticsRejectionRetentionDays),
            AppMonitoringDefaults.MinimumAnalyticsRejectionRetentionDays,
            AppMonitoringDefaults.MaximumAnalyticsRejectionRetentionDays);
        var analyticsPurged = await analyticsRepo.PurgeAsync(
            now.AddDays(-analyticsDetailedDays),
            now.AddDays(-analyticsSessionDays),
            DateOnly.FromDateTime(now).AddMonths(-analyticsAggregateMonths),
            now.AddDays(-analyticsRejectionDays),
            ct).ConfigureAwait(false);
        var keyMaintenance = await analyticsConfiguration.MaintainKeysAsync(now, ct).ConfigureAwait(false);

        var changed = aggregated + metricsAggregated + purgedRaw + purgedHourly + purgedMetricRaw
            + purgedMetricHourly
            + purgedLogs + purgedErrors + purgedVisitors + analyticsPurged.Total
            + keyMaintenance.Rotated + keyMaintenance.PurgedVersions;
        if (changed > 0)
        {
            logger.LogInformation(
                "App telemetry sweep: aggregated {Agg} health + {MetAgg} metric hourly; purged health {PR}/{PH}, metric {PMR}/{PMH}, logs {PL}, errors {PE}, visitor identities {PV}, analytics {PA}; keys rotated {KR}, historical versions purged {KP}",
                aggregated, metricsAggregated, purgedRaw, purgedHourly, purgedMetricRaw, purgedMetricHourly,
                purgedLogs, purgedErrors, purgedVisitors, analyticsPurged.Total,
                keyMaintenance.Rotated, keyMaintenance.PurgedVersions);
        }
        return purgedRaw + purgedHourly + purgedMetricRaw + purgedMetricHourly
               + purgedLogs + purgedErrors + purgedVisitors + analyticsPurged.Total
               + keyMaintenance.PurgedVersions;
    }
}
