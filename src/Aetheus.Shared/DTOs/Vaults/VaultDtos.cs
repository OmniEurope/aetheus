// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record VaultDto : OwnedResourceDto
{
    public int SecretCount { get; init; }
}

public sealed record VaultDetailDto : OwnedResourceDto
{
    public int SecretCount { get; init; }
    public List<VaultSecretDto> Secrets { get; init; } = [];
}

public sealed record VaultSecretDto
{
    public int Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int VersionCount { get; init; }
}

public sealed record VaultSecretVersionDto
{
    public int Version { get; init; }
    public string Key { get; init; } = string.Empty;
    public DateTime ChangedAt { get; init; }
    public ChangeType ChangeType { get; init; }
}

[AtMostOneOwner]
public sealed record CreateVaultRequest : OwnedResourceRequest;

[AtMostOneOwner]
public sealed record UpdateVaultRequest : VersionedOwnedResourceRequest;

public sealed record CreateVaultSecretRequest : KeyValueRequest
{
    public DateTime? ExpiresAt { get; init; }
}

public sealed record UpdateVaultSecretRequest : KeyValueRequest
{
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// Rotation request - same payload as an update but recorded as a Rotated version
/// in the audit history rather than an in-place edit.
/// </summary>
public sealed record RotateVaultSecretRequest
{
    [Required]
    [StringLength(KeyValueRequest.MaxValueLength)]
    public string Value { get; init; } = string.Empty;

    public DateTime? ExpiresAt { get; init; }
}
