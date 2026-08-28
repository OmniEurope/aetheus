// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsAggregate
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public AnalyticsPeriodKind PeriodKind { get; set; }
    public DateOnly PeriodStartUtc { get; set; }
    public int UniqueVisitors { get; set; }
    public int AuthenticatedUniqueVisitors { get; set; }
    public int Sessions { get; set; }
    public int ReturningVisitors { get; set; }
    public long PageViews { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
