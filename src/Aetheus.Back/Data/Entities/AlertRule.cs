// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProvisioningKey { get; set; }
    public int? ServerId { get; set; }
    public MetricType Metric { get; set; }
    public ComparisonOperator Operator { get; set; }
    public double Threshold { get; set; }
    public int SustainedSeconds { get; set; } = 60;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public bool IsEnabled { get; set; } = true;
    public int? NotificationChannelId { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Server? Server { get; set; }
    public NotificationChannel? NotificationChannel { get; set; }
}
