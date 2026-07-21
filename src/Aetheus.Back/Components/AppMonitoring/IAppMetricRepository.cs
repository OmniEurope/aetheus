// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public interface IAppMetricRepository
{
    Task AddSamplesAsync(IEnumerable<AppMetricSample> samples, CancellationToken ct = default);
    Task<List<string>> GetMetricNamesAsync(int appId, CancellationToken ct = default);
    Task<int> CountDistinctMetricNamesAsync(int appId, CancellationToken ct = default);
    Task<List<AppMetricSample>> GetRawSeriesAsync(int appId, string metricName, DateTime since, CancellationToken ct = default);
    Task<List<AppMetricHourly>> GetHourlySeriesAsync(int appId, string metricName, DateTime since, CancellationToken ct = default);

    // Thresholds
    Task<List<AppMetricThreshold>> GetThresholdsForAppAsync(int appId, CancellationToken ct = default);
    /// <summary>All enabled thresholds for an app, tracked (the ingest path mutates IsBreached/LastTriggeredAt).
    /// Loaded once per batch to avoid a per-metric-name N+1 on the hot path.</summary>
    Task<List<AppMetricThreshold>> GetEnabledThresholdsForAppAsync(int appId, CancellationToken ct = default);
    Task<AppMetricThreshold?> GetThresholdForUpdateAsync(int id, CancellationToken ct = default);
    Task<int?> GetThresholdAppIdAsync(int id, CancellationToken ct = default);
    Task AddThresholdAsync(AppMetricThreshold threshold, CancellationToken ct = default);
    Task RemoveThresholdAsync(AppMetricThreshold threshold, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Retention
    Task<int> AggregateRawIntoHourlyAsync(DateTime currentHourStartUtc, DateTime rawFloorUtc, CancellationToken ct = default);
    Task<int> PurgeRawOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> PurgeHourlyOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
