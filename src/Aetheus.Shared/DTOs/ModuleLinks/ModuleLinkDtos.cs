// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record ModuleLinkDto
{
    public int Id { get; init; }
    public ModuleLinkType SourceType { get; init; }
    public string SourceIdentifier { get; init; } = string.Empty;
    public ModuleLinkType TargetType { get; init; }
    public string TargetIdentifier { get; init; } = string.Empty;
    public bool IsAutoDetected { get; init; }
}

public sealed record CreateModuleLinkRequest
{
    [Required]
    public ModuleLinkType SourceType { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string SourceIdentifier { get; init; } = string.Empty;

    [Required]
    public ModuleLinkType TargetType { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string TargetIdentifier { get; init; } = string.Empty;
}

public sealed record LinkedResourceDto
{
    public int LinkId { get; init; }
    public ModuleLinkType Type { get; init; }
    public string Identifier { get; init; } = string.Empty;
    public bool IsAutoDetected { get; init; }
}

public sealed record ModuleLinkPageRequest : PaginationRequest
{
    public ModuleLinkType SourceType { get; init; }

    [Required]
    [MaxLength(2048)]
    [MaxItemStringLength(300)]
    public List<string> ResourceIdentifiers { get; init; } = [];
}
