// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

public sealed record RegistrationTokenDto
{
    public int Id { get; init; }
    public string Token { get; init; } = string.Empty;
    public bool IsUsed { get; init; }
    public int? UsedByServerId { get; init; }
    public int OrganizationId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public sealed record CreateRegistrationTokenRequest
{
    [Range(1, 8760)]
    public int ExpirationHours { get; init; } = 24;

    /// <summary>Organization the registered server will be attached to. Optional -
    /// when omitted, the API falls back to the caller's default organization.</summary>
    [Range(1, int.MaxValue)]
    public int? OrganizationId { get; init; }
}

public sealed record AppSettingDto
{
    [Required]
    [StringLength(200)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(10000)]
    public string Value { get; init; } = string.Empty;
}

public sealed record SecretDto
{
    public int Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record CreateSecretRequest
{
    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(10000)]
    public string Value { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required]
    [StringLength(100)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }

    /// <summary>TOTP code, required when the account has 2FA enabled.</summary>
    [StringLength(10)]
    public string? TotpCode { get; init; }

    /// <summary>Recovery code, used instead of TotpCode when the user has lost their authenticator.</summary>
    [StringLength(20)]
    public string? RecoveryCode { get; init; }
}

public sealed record LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }

    /// <summary>Opaque refresh token for obtaining new access tokens without re-authenticating.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>When true, the caller must re-submit with a TotpCode or RecoveryCode.</summary>
    public bool TotpRequired { get; init; }

    /// <summary>
    /// When true, the authenticated user must change their password before using the app. The
    /// Front routes them to a mandatory change-password screen. The token is still issued so the
    /// change-password call can authenticate.
    /// </summary>
    public bool MustChangePassword { get; init; }
}

public sealed record RefreshTokenRequest
{
    [Required]
    [StringLength(200)]
    public string RefreshToken { get; init; } = string.Empty;
}

// --- TOTP 2FA DTOs (F-010) ---

public sealed record TotpSetupResponse
{
    /// <summary>Base32-encoded shared secret for the authenticator app.</summary>
    public string SharedKey { get; init; } = string.Empty;

    /// <summary>otpauth:// URI for QR code generation.</summary>
    public string AuthenticatorUri { get; init; } = string.Empty;

    /// <summary>One-time recovery codes (plaintext, shown once).</summary>
    public List<string> RecoveryCodes { get; init; } = [];
}

public sealed record TotpVerifyRequest
{
    [Required]
    [StringLength(10)]
    public string Code { get; init; } = string.Empty;
}

public sealed record TotpDisableRequest
{
    [Required]
    [StringLength(200)]
    public string Password { get; init; } = string.Empty;
}

public sealed record ExternalLoginRequest
{
    [Required]
    [StringLength(50)]
    public string Provider { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string SubjectId { get; init; } = string.Empty;

    [StringLength(200)]
    public string? DisplayName { get; init; }

    [StringLength(200)]
    [EmailAddress]
    public string? Email { get; init; }
}

public sealed record DashboardOverviewDto
{
    public int TotalServers { get; init; }
    public int OnlineServers { get; init; }
    public int OfflineServers { get; init; }
    public int PendingTasks { get; init; }
    public int RunningPipelines { get; init; }
    public List<PipelineRunDto> RecentRuns { get; init; } = [];
    public List<ServerDto> Servers { get; init; } = [];
    public List<ProjectDto> Projects { get; init; } = [];
}
