// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record VariableLibraryDto
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
    public int EntryCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid RowVersion { get; init; }
}

public sealed record VariableLibraryDetailDto
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
    public int EntryCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid RowVersion { get; init; }
    public List<VariableEntryDto> Entries { get; init; } = [];
}

public sealed record VariableEntryDto
{
    public int Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int VersionCount { get; init; }
}

public sealed record VariableEntryVersionDto
{
    public int Version { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public DateTime ChangedAt { get; init; }
    public ChangeType ChangeType { get; init; }
}

[ExactlyOneOwner]
public sealed record CreateVariableLibraryRequest
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
public sealed record UpdateVariableLibraryRequest
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

public sealed record CreateVariableEntryRequest
{
    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Value { get; init; } = string.Empty;
}

public sealed record UpdateVariableEntryRequest
{
    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Value { get; init; } = string.Empty;
}
