// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.PersonalAccessTokens;

/// <summary>
/// Shared constants for the Personal Access Token authentication path (PLAN-006 4.5).
/// </summary>
public static class PatConstants
{
    /// <summary>Recognisable, non-secret prefix on every PAT plaintext. Lets the JWT bearer handler
    /// forward PAT-shaped tokens to the PAT scheme instead of trying (and failing) to validate them as JWTs.</summary>
    public const string TokenPrefix = "aeth_pat_";

    /// <summary>Authentication scheme name registered in <c>Program.cs</c>.</summary>
    public const string SchemeName = "PersonalAccessToken";

    /// <summary>Claim carrying the PAT scope on the resolved principal (read by the enforcement middleware).</summary>
    public const string ScopeClaimType = "aetheus:pat_scope";

    /// <summary>Claim carrying the PAT id (for auditing which token made a request).</summary>
    public const string TokenIdClaimType = "aetheus:pat_id";

    public const string ReadOnlyScopeValue = "readonly";
    public const string ReadWriteScopeValue = "readwrite";

    /// <summary>Length of the stored, non-secret display prefix: <c>aeth_pat_</c> (9) + 4 random chars.</summary>
    public const int PrefixDisplayLength = 13;
}

/// <summary>
/// Live identity resolved from a valid PAT: the owner and their CURRENT roles (freshly loaded, so a
/// PAT can never outrank what the user holds right now), plus the token's scope and id.
/// </summary>
public sealed record PatPrincipal(
    int UserId,
    string Username,
    IReadOnlyList<string> Roles,
    Aetheus.Shared.Enums.PatScope Scope,
    int TokenId);
