// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class RegistrationToken
{
    public int Id { get; set; }

    /// <summary>Stores the SHA-256/HMAC HASH of the registration token, never the plaintext (mirrors
    /// <c>ServerToken.TokenHash</c> / <c>RefreshToken.TokenHash</c>); the column name is kept for
    /// migration stability. The plaintext is shown once at creation and never persisted.</summary>
    public string Token { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public int? UsedByServerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    // Navigation
    public Server? UsedByServer { get; set; }
}
