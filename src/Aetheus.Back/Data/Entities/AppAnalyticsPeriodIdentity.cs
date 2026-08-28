// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AppAnalyticsPeriodIdentity
{
    public long Id { get; set; }
    public int MonitoredAppId { get; set; }
    public AnalyticsPeriodKind PeriodKind { get; set; }
    public DateOnly PeriodStartUtc { get; set; }
    public string Pseudonym { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public int KeyVersion { get; set; }
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
