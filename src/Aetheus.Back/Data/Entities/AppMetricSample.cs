// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// The OTLP metric point type. <see cref="Sum"/> is a monotonic cumulative counter (since process
/// start): its raw <see cref="AppMetricSample.Value"/> must be turned into a delta/rate between
/// consecutive points before display, never plotted as-is. <see cref="Gauge"/> and <see cref="Histogram"/>
/// (already reduced to a p95 estimate at ingest) are instantaneous and are plotted as-is.
/// </summary>
public enum MetricKind
{
    Gauge = 0,
    Sum = 1,
    Histogram = 2
}

/// <summary>
/// A raw application metric data point ingested via OTLP (ADR-021 phase 2, 7-day retention).
/// <see cref="Timestamp"/> is emit-sourced (from the OTLP data point), so it is excluded from the
/// auto-stamping in <c>AppDbContext.SaveChangesAsync</c>.
/// </summary>
public class AppMetricSample
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateTime Timestamp { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Unit { get; set; }
    public string? AttributesJson { get; set; }
    public MetricKind Kind { get; set; } = MetricKind.Gauge;

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
