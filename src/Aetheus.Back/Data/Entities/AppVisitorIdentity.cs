// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// One privacy-preserving unique visitor for one UTC day. The identifier is already a daily keyed
/// digest when received and is hashed again by Aetheus before persistence. It cannot be correlated
/// across days and contains no raw IP address, user agent, or browser identifier.
/// </summary>
public class AppVisitorIdentity
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateOnly DayUtc { get; set; }
    public string FingerprintHash { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }

    public MonitoredApp MonitoredApp { get; set; } = null!;
}
