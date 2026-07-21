// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A raw application metric data point ingested via OTLP (PLAN-001 phase 2, 7-day retention).
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

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
