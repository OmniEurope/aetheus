// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Persisted refresh token for JWT rotation (F-012). Each login issues a refresh token
/// alongside the short-lived JWT. On refresh, the old token is revoked and a new pair
/// (access + refresh) is issued. A 5-minute grace window allows in-flight requests
/// using the old refresh token to still succeed.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>SHA-256 hash of the plaintext token. Plaintext is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>When this token was rotated, points to its replacement.</summary>
    public int? ReplacedById { get; set; }

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTime now) => ExpiresAt <= now;
    public bool IsActive(DateTime now) => !IsRevoked && !IsExpired(now);

    // Navigation
    public User User { get; set; } = null!;
    public RefreshToken? ReplacedBy { get; set; }
}
