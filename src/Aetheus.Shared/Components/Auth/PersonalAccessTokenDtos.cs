// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aetheus.Shared.Components.Auth;

/// <summary>
/// Metadata view of a Personal Access Token (ADR-024 4.5). Never carries the plaintext token -
/// only a short non-secret <see cref="TokenPrefix"/> for identification. The plaintext is returned
/// exactly once, at creation, via <see cref="CreatedPersonalAccessTokenDto"/>.
/// </summary>
public sealed record PersonalAccessTokenDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public PatScope Scope { get; init; }

    /// <summary>Short, non-secret leading fragment (e.g. <c>aeth_pat_ab12</c>) shown so the owner can
    /// tell their tokens apart. The secret body is never persisted or returned after creation.</summary>
    public string TokenPrefix { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}

/// <summary>
/// Creation response. Carries the FULL plaintext <see cref="PlaintextToken"/> exactly once - the
/// database only ever stores an HMAC-SHA256 hash. The caller must surface it to the user immediately;
/// it can never be retrieved again.
/// </summary>
public sealed record CreatedPersonalAccessTokenDto
{
    public PersonalAccessTokenDto Token { get; init; } = new();

    /// <summary>Plaintext token, shown once. Prefixed <c>aeth_pat_</c>. Never logged, never persisted.</summary>
    [SuppressMessage("Aetheus.Security", "SEC004",
        Justification = "Issuance response. Returning the plaintext exactly once, to the caller that just minted it, is what this contract exists for; the database keeps only its HMAC-SHA256 hash and no other endpoint can ever read it back.")]
    public string PlaintextToken { get; init; } = string.Empty;
}

/// <summary>Request to mint a Personal Access Token for the calling user.</summary>
public sealed record CreatePersonalAccessTokenRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    public PatScope Scope { get; init; } = PatScope.ReadOnly;

    /// <summary>Lifetime in days (1-90). A replacement can overlap for at most seven days.</summary>
    [Range(1, 90)]
    public int ExpirationDays { get; init; } = 30;
}
