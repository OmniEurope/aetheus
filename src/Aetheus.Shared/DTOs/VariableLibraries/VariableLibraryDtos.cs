// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record VariableLibraryDto : OwnedResourceDto
{
    public int EntryCount { get; init; }
}

public sealed record VariableLibraryDetailDto : OwnedResourceDto
{
    public int EntryCount { get; init; }
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

[AtMostOneOwner]
public sealed record CreateVariableLibraryRequest : OwnedResourceRequest;

[AtMostOneOwner]
public sealed record UpdateVariableLibraryRequest : VersionedOwnedResourceRequest;

public sealed record CreateVariableEntryRequest : KeyValueRequest;

public sealed record UpdateVariableEntryRequest : KeyValueRequest;
