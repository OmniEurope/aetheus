// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// One metric name a monitored application sends (recette R-477). The list of an application's names
/// used to be a <c>SELECT DISTINCT</c> over its whole detailed-retention window of samples, paid by the
/// metric explorer on every opening (1.95 s in production) and, once an hour, by an ingestion batch
/// holding the application's lock. The name is written here the first time it is ingested and removed
/// by the retention sweep once no sample carries it any more.
/// </summary>
public class AppMetricName
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public string Name { get; set; } = string.Empty;

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
