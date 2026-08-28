// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Hourly aggregate of an application metric (ADR-021 phase 2, 90-day retention). Produced by
/// <c>AppTelemetryRetentionService</c> from raw <see cref="AppMetricSample"/> rows of completed hours.
/// </summary>
public class AppMetricHourly
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public string MetricName { get; set; } = string.Empty;

    /// <summary>Start of the aggregated hour (UTC, truncated).</summary>
    public DateTime HourUtc { get; set; }

    public int SampleCount { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double AvgValue { get; set; }
    public double P95Value { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
