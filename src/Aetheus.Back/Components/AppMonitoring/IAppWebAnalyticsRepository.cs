// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AppMonitoring;

public readonly record struct WebAnalyticsPurgeResult(
    int Events,
    int Sessions,
    int PeriodIdentities,
    int Aggregates,
    int Pages,
    int Rejections)
{
    public int Total => Events + Sessions + PeriodIdentities + Aggregates + Pages + Rejections;
}

public interface IAppWebAnalyticsRepository
{
    Task<(int Accepted, int Replayed)> RecordAsync(
        int appId,
        IReadOnlyList<AppWebAnalyticsIngestEvent> events,
        int sessionTimeoutMinutes,
        DateTime receivedAtUtc,
        CancellationToken ct = default);

    Task<long> EstimateStorageBytesAsync(int appId, CancellationToken ct = default);
    Task<HashSet<string>> GetRouteNamesAsync(int appId, CancellationToken ct = default);
    Task RecordRejectionAsync(int appId, string reasonCode, int count, DateTime occurredAtUtc, CancellationToken ct = default);
    Task<AppWebAnalyticsSummaryDto> GetSummaryAsync(
        int appId,
        DateOnly todayUtc,
        DateOnly weekStartUtc,
        DateOnly monthStartUtc,
        DateOnly historyStartUtc,
        long storageBudgetBytes,
        long rejectedEvents,
        DateTime? lastIngestAtUtc,
        CancellationToken ct = default);

    Task<WebAnalyticsPurgeResult> PurgeAsync(
        DateTime eventCutoffUtc,
        DateTime sessionCutoffUtc,
        DateOnly aggregateCutoffUtc,
        DateTime rejectionCutoffUtc,
        CancellationToken ct = default);
}
