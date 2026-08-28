// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

public abstract record OwnedResourceDto
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
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid RowVersion { get; init; }
}

public abstract record OwnedResourceRequest
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

public abstract record VersionedOwnedResourceRequest : OwnedResourceRequest
{
    public Guid RowVersion { get; init; }
}

public abstract record KeyValueRequest
{
    public const int MaxValueLength = 10_000;

    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(MaxValueLength)]
    public string Value { get; init; } = string.Empty;
}
