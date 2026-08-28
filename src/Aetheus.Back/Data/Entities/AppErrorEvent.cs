// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// An application error grouped by fingerprint (ADR-021 phase 4). Built from OTLP error spans /
/// exception events: identical (exception type + top frame) errors collapse into one row whose
/// <see cref="OccurrenceCount"/> increments, mirroring an issue-tracker's grouping.
/// </summary>
public class AppErrorEvent
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }

    /// <summary>Stable grouping key: hash of (exception type + top frame).</summary>
    public string Fingerprint { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TopFrame { get; set; }

    public int OccurrenceCount { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
