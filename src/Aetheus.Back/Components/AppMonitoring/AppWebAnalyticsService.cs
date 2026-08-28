// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Notifications;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppWebAnalyticsService(
    IAppWebAnalyticsRepository repository,
    IAppMonitoringRepository apps,
    INotificationService notifications,
    AppIngestGate ingestGate,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAppWebAnalyticsService
{
    public async Task<WebAnalyticsIngestOutcome> IngestAsync(
        int appId,
        IReadOnlyList<AppWebAnalyticsIngestEvent> events,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var gate = ingestGate.For(appId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var app = await apps.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
            if (app is null)
                return new WebAnalyticsIngestOutcome(0, 0, events.Count);

            var capped = events.Take(AppMonitoringDefaults.MaximumAnalyticsBatchSize).ToList();
            var rejected = events.Count - capped.Count;
            var invalid = capped.RemoveAll(item => !IsAllowed(item, app, now));
            rejected += invalid;
            var routes = await repository.GetRouteNamesAsync(appId, ct).ConfigureAwait(false);
            var cardinalityRejected = capped.RemoveAll(item =>
            {
                if (routes.Contains(item.Route))
                    return false;
                if (routes.Count >= AppMonitoringDefaults.MaximumAnalyticsRoutesPerApp)
                    return true;
                routes.Add(item.Route);
                return false;
            });
            rejected += cardinalityRejected;
            if (rejected > 0)
            {
                app.AnalyticsRejectedCount += rejected;
                await repository.RecordRejectionAsync(
                    appId,
                    cardinalityRejected > 0 ? "cardinality" : "policy",
                    rejected,
                    now,
                    ct).ConfigureAwait(false);
            }

            var storageBytes = await repository.EstimateStorageBytesAsync(appId, ct).ConfigureAwait(false);
            var budgetBytes = app.AnalyticsStorageBudgetBytes > 0
                ? app.AnalyticsStorageBudgetBytes
                : AppMonitoringDefaults.DefaultAnalyticsStorageBudgetBytes;
            var usagePercent = (int)Math.Min(100, storageBytes * 100 / budgetBytes);
            var alertLevel = usagePercent >= 95 ? 95 : usagePercent >= 85 ? 85 : usagePercent >= 70 ? 70 : 0;
            if (alertLevel > app.AnalyticsQuotaAlertLevel)
            {
                app.AnalyticsQuotaAlertLevel = alertLevel;
                await notifications.SendEventAsync("app.analytics.quota", new
                {
                    MonitoredAppId = appId,
                    UsagePercent = usagePercent,
                    Threshold = alertLevel
                }, ct).ConfigureAwait(false);
            }

            if (usagePercent >= 100)
            {
                app.AnalyticsRejectedCount += capped.Count;
                await repository.RecordRejectionAsync(appId, "quota", capped.Count, now, ct).ConfigureAwait(false);
                await apps.SaveChangesAsync(ct).ConfigureAwait(false);
                return new WebAnalyticsIngestOutcome(0, 0, rejected + capped.Count);
            }

            var sessionTimeout = Math.Clamp(
                configuration.GetValue(
                    "AppMonitoring:Analytics:SessionTimeoutMinutes",
                    AppMonitoringDefaults.DefaultAnalyticsSessionTimeoutMinutes),
                AppMonitoringDefaults.MinimumAnalyticsSessionTimeoutMinutes,
                AppMonitoringDefaults.MaximumAnalyticsSessionTimeoutMinutes);
            var (accepted, replayed) = await repository.RecordAsync(
                appId,
                capped,
                sessionTimeout,
                now,
                ct).ConfigureAwait(false);
            if (replayed > 0)
            {
                app.AnalyticsRejectedCount += replayed;
                await repository.RecordRejectionAsync(appId, "replay", replayed, now, ct).ConfigureAwait(false);
            }
            if (accepted > 0)
                app.AnalyticsLastIngestAt = now;
            await apps.SaveChangesAsync(ct).ConfigureAwait(false);
            return new WebAnalyticsIngestOutcome(accepted, replayed, rejected);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AppWebAnalyticsSummaryDto?> GetSummaryAsync(
        int appId,
        int days,
        CancellationToken ct = default)
    {
        var app = await apps.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
        if (app is null)
            return null;

        days = Math.Clamp(days, 1, 90);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        return await repository.GetSummaryAsync(
            appId,
            today,
            AppWebAnalyticsRepository.WeekStart(now),
            new DateOnly(now.Year, now.Month, 1),
            today.AddDays(-(days - 1)),
            app.AnalyticsStorageBudgetBytes > 0
                ? app.AnalyticsStorageBudgetBytes
                : AppMonitoringDefaults.DefaultAnalyticsStorageBudgetBytes,
            app.AnalyticsRejectedCount,
            app.AnalyticsLastIngestAt,
            ct).ConfigureAwait(false);
    }

    private static bool IsAllowed(
        AppWebAnalyticsIngestEvent item,
        Aetheus.Back.Data.Entities.MonitoredApp app,
        DateTime now) =>
        HasValidEnvelope(item, app)
        && HasAcceptedKey(item, app, now)
        && IsSupportedPayload(item)
        && IsCurrent(item, now)
        && IsSafeRoute(item.Route);

    private static bool HasValidEnvelope(
        AppWebAnalyticsIngestEvent item,
        Aetheus.Back.Data.Entities.MonitoredApp app) =>
        app.AnalyticsEnabled
        && item.SchemaVersion == 1
        && item.EventId != Guid.Empty
        && item.ApplicationId == app.Id;

    private static bool HasAcceptedKey(
        AppWebAnalyticsIngestEvent item,
        Aetheus.Back.Data.Entities.MonitoredApp app,
        DateTime now) =>
        item.KeyVersion > 0
        && item.KeyVersion <= app.AnalyticsPseudonymKeyVersion
        && (item.KeyVersion == app.AnalyticsPseudonymKeyVersion
            || app.AnalyticsPseudonymKeyCreatedAt is { } createdAt && createdAt > now.AddDays(-31));

    private static bool IsCurrent(AppWebAnalyticsIngestEvent item, DateTime now) =>
        item.OccurredAtUtc >= now.AddMinutes(-5)
        && item.OccurredAtUtc <= now.AddMinutes(5);

    private static bool IsSafeRoute(string route) =>
        route.StartsWith("/", StringComparison.Ordinal)
        && !route.Contains('?', StringComparison.Ordinal)
        && !route.Contains('#', StringComparison.Ordinal)
        && !route.Contains('@', StringComparison.Ordinal);

    private static bool IsSupportedPayload(AppWebAnalyticsIngestEvent item) =>
        item.Kind switch
        {
            "page_view" => item.DurationMs is null && item.ErrorType is null,
            "browser_performance" => item.DurationMs is >= 0 and <= 300_000
                                     && item.ErrorType is null,
            "browser_error" => item.DurationMs is null
                               && item.ErrorType is "script_error"
                                   or "unhandled_rejection",
            _ => false
        };
}
