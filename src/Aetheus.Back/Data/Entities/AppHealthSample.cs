// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A single raw availability data point for a <see cref="MonitoredApp"/> (7-day retention).
/// <see cref="Timestamp"/> is probe-sourced (the moment of the check), so it is excluded from the
/// auto-stamping in <c>AppDbContext.SaveChangesAsync</c>, mirroring <see cref="ServerMetric"/>.
/// </summary>
public class AppHealthSample
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsUp { get; set; }
    public int? ResponseTimeMs { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
