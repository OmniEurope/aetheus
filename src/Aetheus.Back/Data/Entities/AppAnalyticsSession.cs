// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsSession
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public string SessionPseudonym { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public int PageViewCount { get; set; }
    public bool Authenticated { get; set; }
    public bool ReturningVisitor { get; set; }
    public int KeyVersion { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
