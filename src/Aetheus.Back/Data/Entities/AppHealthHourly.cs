// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Hourly availability aggregate for a <see cref="MonitoredApp"/> (90-day retention). Produced by
/// <c>AppTelemetryRetentionService</c> from raw <see cref="AppHealthSample"/> rows older than one hour.
/// </summary>
public class AppHealthHourly
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }

    /// <summary>Start of the aggregated hour (UTC, truncated to the hour).</summary>
    public DateTime HourUtc { get; set; }

    public int SampleCount { get; set; }
    public int UpCount { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double P95ResponseTimeMs { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
