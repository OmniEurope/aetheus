// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsPageAggregate
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public DateOnly DayUtc { get; set; }
    public string Route { get; set; } = string.Empty;
    public long PageViews { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
