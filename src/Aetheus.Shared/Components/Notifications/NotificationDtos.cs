// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Notifications;

public sealed record NotificationChannelDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public NotificationChannelType Type { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
    public bool IsEnabled { get; init; }
    public int RuleCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record NotificationRuleDto
{
    public int Id { get; init; }
    public int NotificationChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string? FilterJson { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record NotificationTestResultDto
{
    public NotificationTestStatus Status { get; init; }
    public string? Message { get; init; }
}

public sealed record CreateNotificationChannelRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    public NotificationChannelType Type { get; init; }

    [Required]
    [StringLength(4000)]
    public string ConfigurationJson { get; init; } = "{}";
}

public sealed record UpdateNotificationChannelRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string ConfigurationJson { get; init; } = "{}";

    public bool IsEnabled { get; init; } = true;
}

public sealed record CreateNotificationRuleRequest
{
    public int NotificationChannelId { get; init; }

    [Required]
    [StringLength(100)]
    public string EventType { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? FilterJson { get; init; }
}

public sealed record UpdateNotificationRuleRequest
{
    [Required]
    [StringLength(100)]
    public string EventType { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? FilterJson { get; init; }

    public bool IsEnabled { get; init; } = true;
}
