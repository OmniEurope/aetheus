// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// One notification produced for one user, kept whether or not a transport carried it. The row is
/// what lets a user follow every notification raised for them: <see cref="Status"/> says what really
/// happened, and is never <see cref="NotificationDeliveryStatus.Sent"/> unless a transport accepted it.
/// </summary>
public class NotificationDelivery
{
    public int Id { get; set; }
    public int RecipientUserId { get; set; }

    /// <summary>The admin channel that carried it, or null when no channel was involved (project subscription).</summary>
    public int? ChannelId { get; set; }

    public string EventType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public NotificationDeliveryStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public User Recipient { get; set; } = default!;
    public NotificationChannel? Channel { get; set; }
}
