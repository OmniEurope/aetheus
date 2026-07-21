// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class WebhookSubscription
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>HMAC signing secret for outbound webhooks. Stored AES-256 encrypted (must stay
    /// decryptable, not hashed); the encryption happens in the service layer (WebhookService.EncryptValue),
    /// which is why it is not named <c>Encrypted*</c> like the other at-rest secret columns.</summary>
    public string? Secret { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int FailureCount { get; set; }
}
