// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

public sealed record WebhookSubscriptionDto
{
    public int Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string TargetUrl { get; init; } = string.Empty;
    public bool HasSecret { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? LastTriggeredAt { get; init; }
    public int FailureCount { get; init; }
}

public sealed record CreateWebhookSubscriptionRequest
{
    [Required]
    [StringLength(100)]
    public string EventType { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    [Url]
    public string TargetUrl { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Secret { get; init; }
}

public sealed record UpdateWebhookSubscriptionRequest
{
    [Required]
    [StringLength(100)]
    public string EventType { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    [Url]
    public string TargetUrl { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Secret { get; init; }

    public bool IsEnabled { get; init; } = true;
}
