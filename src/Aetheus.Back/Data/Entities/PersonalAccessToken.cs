// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A per-user Personal Access Token (ADR-024 4.5) - a second authentication path for scripts,
/// external CI, and the future CLI. Follows the <see cref="RegistrationToken"/> / <see cref="RefreshToken"/>
/// lifecycle: only an HMAC-SHA256 <see cref="TokenHash"/> is stored, the plaintext is shown once at
/// creation and never persisted. Scope is always re-intersected with the user's CURRENT RBAC at
/// request time, so a PAT can never exceed what its owner currently holds.
/// </summary>
public class PersonalAccessToken
{
    public int Id { get; set; }

    /// <summary>Owning user. The PAT resolves to this user's live identity and roles on every request.</summary>
    public int UserId { get; set; }

    /// <summary>Human-friendly label chosen by the owner to tell tokens apart.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 hash of the plaintext token (keyed by the JWT signing key), never the plaintext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Short, non-secret leading fragment of the plaintext (e.g. <c>aeth_pat_ab12</c>) for display.</summary>
    public string TokenPrefix { get; set; } = string.Empty;

    public PatScope Scope { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Last time the token successfully authenticated a request (throttled write; usage trace).</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Set when the owner revokes the token; a revoked token is inert immediately.</summary>
    public DateTime? RevokedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTime now) => ExpiresAt <= now;
    public bool IsActive(DateTime now) => !IsRevoked && !IsExpired(now);
}
