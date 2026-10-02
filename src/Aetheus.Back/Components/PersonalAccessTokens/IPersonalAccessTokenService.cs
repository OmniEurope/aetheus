// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Auth;

namespace Aetheus.Back.Components.PersonalAccessTokens;

public interface IPersonalAccessTokenService
{
    /// <summary>Mints a PAT for <paramref name="userId"/>. The returned DTO carries the plaintext exactly once.</summary>
    Task<CreatedPersonalAccessTokenDto> CreateAsync(int userId, CreatePersonalAccessTokenRequest request, CancellationToken ct = default);

    Task<PaginatedResult<PersonalAccessTokenDto>> GetForUserAsync(
        int userId, PaginationRequest request, CancellationToken ct = default);

    /// <summary>Revokes the token if it belongs to the user and is not already revoked. Returns false otherwise.</summary>
    Task<bool> RevokeAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Resolves a presented plaintext PAT to its owner's live identity + roles, or null if invalid.
    /// Also records a throttled usage trace (<c>LastUsedAt</c>).</summary>
    Task<PatPrincipal?> ValidateAsync(string plaintextToken, CancellationToken ct = default);
}
