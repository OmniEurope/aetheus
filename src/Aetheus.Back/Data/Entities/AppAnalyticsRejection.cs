// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsRejection
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public int Count { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
