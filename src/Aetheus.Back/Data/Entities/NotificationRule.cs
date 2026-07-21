// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class NotificationRule
{
    public int Id { get; set; }
    public int NotificationChannelId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FilterJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public NotificationChannel Channel { get; set; } = null!;
}
