// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class NotificationChannel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NotificationChannelType Type { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public List<NotificationRule> Rules { get; set; } = [];
}
