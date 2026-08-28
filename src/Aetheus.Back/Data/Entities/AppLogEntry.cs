// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// An application log record ingested via OTLP (ADR-021 phase 3, 7-day retention, no aggregate).
/// <see cref="Timestamp"/> is emit-sourced, excluded from auto-stamping in <c>AppDbContext.SaveChangesAsync</c>.
/// </summary>
public class AppLogEntry
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>OTLP severity number (1..24); 0 when unspecified.</summary>
    public int SeverityNumber { get; set; }
    public string? SeverityText { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? AttributesJson { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
