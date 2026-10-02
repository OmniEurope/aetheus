// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public interface IAppMetricRepository
{
    Task AddSamplesAsync(IEnumerable<AppMetricSample> samples, CancellationToken ct = default);
    Task<List<string>> GetMetricNamesAsync(int appId, CancellationToken ct = default);

    /// <summary>Records the names an ingestion saw for the first time; a name already recorded (by this
    /// instance or by the other colour) is left as it is.</summary>
    Task AddMetricNamesAsync(int appId, IReadOnlyCollection<string> names, CancellationToken ct = default);

    /// <summary>Removes the names no sample of their application carries any more. Returns how many.</summary>
    Task<int> PruneMetricNamesAsync(CancellationToken ct = default);
    Task<int> CountDistinctMetricNamesAsync(int appId, CancellationToken ct = default);
    Task<List<AppMetricSample>> GetRawSeriesAsync(int appId, string metricName, string? attributesJson, DateTime since, CancellationToken ct = default);
    Task<List<AppMetricHourly>> GetHourlySeriesAsync(int appId, string metricName, string? attributesJson, DateTime since, CancellationToken ct = default);
    /// <summary>Distinct attribute combinations recorded for this metric name (raw + hourly), bounded, so
    /// the UI can offer a series picker instead of merging unrelated attribute sets into one line.</summary>
    Task<List<string?>> GetMetricAttributeGroupsAsync(int appId, string metricName, CancellationToken ct = default);

    /// <summary>R-455: the newest point of each series of <paramref name="metricNames"/> among the samples
    /// written within <paramref name="exportSpan"/> of the newest one, i.e. the application's last export.</summary>
    Task<List<AppMetricSample>> GetLatestExportAsync(int appId, IReadOnlyCollection<string> metricNames, TimeSpan exportSpan, CancellationToken ct = default);

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
    /// <summary>R2-020: rolls up one completed hour, application by application; a no-op for the rolled-up ones.</summary>
    Task<int> AggregateHourAsync(DateTime hourUtc, CancellationToken ct = default);
    Task<int> PurgeRawOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> PurgeHourlyOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
