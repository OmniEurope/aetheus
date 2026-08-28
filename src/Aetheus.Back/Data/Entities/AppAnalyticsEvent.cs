// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsEvent
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public Guid EventId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int? DurationMs { get; set; }
    public string? ErrorType { get; set; }
    public string SessionPseudonym { get; set; } = string.Empty;
    public bool Authenticated { get; set; }
    public int KeyVersion { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
