// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Auth;

public interface IAuthService
{
    Task<bool> IsRegistrationTokenValidAsync(string token, CancellationToken ct = default);
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<RegistrationTokenDto> CreateRegistrationTokenAsync(CreateRegistrationTokenRequest request, CancellationToken ct = default);
    Task<List<RegistrationTokenDto>> GetRegistrationTokensAsync(CancellationToken ct = default);
    Task<RegistrationTokenDto?> GetRegistrationTokenAsync(int id, CancellationToken ct = default);
    Task<int?> ValidateServerTokenAsync(string token, CancellationToken ct = default);
    Task<ServerRegistrationResponse?> RotateAgentTokenAsync(int serverId, CancellationToken ct = default);

    /// <summary>
    /// If the server's freshest active agent token is within the renewal window,
    /// mints and persists a new one (WITHOUT revoking the old - fail-safe) and
    /// returns it for the agent to adopt. Returns null when no renewal is due.
    /// </summary>
    Task<ServerHeartbeatResponseDto?> MaybeRenewAgentTokenAsync(int serverId, CancellationToken ct = default);
    Task<LoginResponse?> HandleExternalLoginAsync(string provider, string subjectId, string? displayName, string? email, CancellationToken ct = default);
    Task<bool> IsSecurityStampValidAsync(int userId, string stamp, CancellationToken ct = default);
    Task BumpSecurityStampAsync(int userId, CancellationToken ct = default);
    Task<LoginResponse?> RenewTokenAsync(int userId, CancellationToken ct = default);

    // --- Refresh Token Rotation (F-012) ---
    Task<LoginResponse?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    // --- TOTP 2FA (F-010) ---
    Task<TotpSetupResponse> SetupTotpAsync(int userId, CancellationToken ct = default);
    Task<bool> VerifyAndEnableTotpAsync(int userId, string code, CancellationToken ct = default);
    Task<bool> DisableTotpAsync(int userId, string password, CancellationToken ct = default);

    // --- Lockout (F-011) ---
    Task<bool> UnlockUserAsync(int userId, CancellationToken ct = default);

    /// <summary>Validates username/password for Basic-auth flows (e.g. Git Smart HTTP).</summary>
    Task<bool> ValidateBasicAuthAsync(string username, string password, CancellationToken ct = default);
}
