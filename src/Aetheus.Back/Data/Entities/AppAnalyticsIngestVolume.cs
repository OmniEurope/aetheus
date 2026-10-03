// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Audit R2-007 follow-up: how many audience events one accepted batch stored, and when. The rolling
/// hour of an application is the sum of its rows of the last hour; read from the shared database, it
/// holds across both blue-green colours, where a process counter would only see half the traffic.
/// </summary>
public class AppAnalyticsIngestVolume
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public int Count { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
