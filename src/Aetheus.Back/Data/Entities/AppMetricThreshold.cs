// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A per-app, per-metric alert threshold (PLAN-001 phase 2). When the latest ingested value for
/// <see cref="MetricName"/> breaches the comparison, an <c>app.metric.threshold</c> notification fires.
/// </summary>
public class AppMetricThreshold
{
    public int Id { get; set; }
    public int MonitoredAppId { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; }
    public double Threshold { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Currently breached? Used to fire once on entering breach, not on every sample.</summary>
    public bool IsBreached { get; set; }
    public DateTime? LastTriggeredAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public MonitoredApp MonitoredApp { get; set; } = null!;
}
