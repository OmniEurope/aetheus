// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record VaultDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? EnvironmentId { get; init; }
    public string? EnvironmentName { get; init; }
    public int? ProjectServerId { get; init; }
    public string? ProjectServerName { get; init; }
    public int SecretCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid RowVersion { get; init; }
}

public sealed record VaultDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? EnvironmentId { get; init; }
    public string? EnvironmentName { get; init; }
    public int? ProjectServerId { get; init; }
    public string? ProjectServerName { get; init; }
    public int SecretCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid RowVersion { get; init; }
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

[ExactlyOneOwner]
public sealed record CreateVaultRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
}

[ExactlyOneOwner]
public sealed record UpdateVaultRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }

    public Guid RowVersion { get; init; }
}

public sealed record CreateVaultSecretRequest
{
    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Value { get; init; } = string.Empty;

    public DateTime? ExpiresAt { get; init; }
}

public sealed record UpdateVaultSecretRequest
{
    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Value { get; init; } = string.Empty;

    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// Rotation request - same payload as an update but recorded as a Rotated version
/// in the audit history rather than an in-place edit.
/// </summary>
public sealed record RotateVaultSecretRequest
{
    [Required]
    [StringLength(4000)]
    public string Value { get; init; } = string.Empty;

    public DateTime? ExpiresAt { get; init; }
}
