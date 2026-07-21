// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Shared.Constants;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// Hourly sweep (PLAN-001 phase 1): aggregates raw <c>AppHealthSample</c> rows from completed hours into
/// <c>AppHealthHourly</c>, then purges raw samples older than the raw-retention window (default 7 days) and
/// hourly aggregates older than the aggregate-retention window (default 90 days). Bounded PG growth per ADR-001.
/// </summary>
public sealed class AppTelemetryRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AppTelemetryRetentionService> logger,
    TimeProvider timeProvider) : BackgroundService
{
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
        try
        {
            await SweepAsync(ct, catchUp).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during app telemetry retention sweep");
        }
    }

    internal async Task SweepAsync(CancellationToken ct, bool catchUp = false)
    {
        var rawRetentionDays = Math.Clamp(
            configuration.GetValue("AppMonitoring:RawRetentionDays", AppMonitoringDefaults.DefaultRawRetentionDays),
            AppMonitoringDefaults.MinimumRawRetentionDays,
            AppMonitoringDefaults.MaximumRawRetentionDays);
        var hourlyRetentionDays = Math.Clamp(
            configuration.GetValue("AppMonitoring:HourlyRetentionDays", AppMonitoringDefaults.DefaultHourlyRetentionDays),
            AppMonitoringDefaults.MinimumHourlyRetentionDays,
            AppMonitoringDefaults.MaximumHourlyRetentionDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppMonitoringRepository>();
        var metricRepo = scope.ServiceProvider.GetRequiredService<IAppMetricRepository>();
        var logRepo = scope.ServiceProvider.GetRequiredService<IAppLogRepository>();
        var errorRepo = scope.ServiceProvider.GetRequiredService<IAppErrorRepository>();

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

        // Availability (phase 1)
        var aggregated = await repo.AggregateRawIntoHourlyAsync(currentHourStart, aggFloor, ct).ConfigureAwait(false);
        var purgedRaw = await repo.PurgeRawOlderThanAsync(rawCutoff, ct).ConfigureAwait(false);
        var purgedHourly = await repo.PurgeHourlyOlderThanAsync(hourlyCutoff, ct).ConfigureAwait(false);

        // Metrics (phase 2): raw -> hourly, purge both
        var metricsAggregated = await metricRepo.AggregateRawIntoHourlyAsync(currentHourStart, aggFloor, ct).ConfigureAwait(false);
        var purgedMetricRaw = await metricRepo.PurgeRawOlderThanAsync(rawCutoff, ct).ConfigureAwait(false);
        var purgedMetricHourly = await metricRepo.PurgeHourlyOlderThanAsync(hourlyCutoff, ct).ConfigureAwait(false);

        // Logs (phase 3): purge only (no aggregate). Errors (phase 4): purge groups not seen in the raw window.
        var purgedLogs = await logRepo.PurgeOlderThanAsync(rawCutoff, ct).ConfigureAwait(false);
        var purgedErrors = await errorRepo.PurgeOlderThanAsync(hourlyCutoff, ct).ConfigureAwait(false);

        if (aggregated + metricsAggregated + purgedRaw + purgedHourly + purgedMetricRaw + purgedMetricHourly + purgedLogs + purgedErrors > 0)
        {
            logger.LogInformation(
                "App telemetry sweep: aggregated {Agg} health + {MetAgg} metric hourly; purged health {PR}/{PH}, metric {PMR}/{PMH}, logs {PL}, errors {PE}",
                aggregated, metricsAggregated, purgedRaw, purgedHourly, purgedMetricRaw, purgedMetricHourly, purgedLogs, purgedErrors);
        }
    }
}
