// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

// --- A-01: Package Feeds ---

public abstract record PackageFeedBaseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public PackageFeedType FeedType { get; init; }
    public string UpstreamUrl { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? ServiceConnectionName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record PackageFeedDto : PackageFeedBaseDto
{
    public int PackageCount { get; init; }
}

public sealed record PackageFeedDetailDto : PackageFeedBaseDto
{
    public int? ServiceConnectionId { get; init; }
    public List<PackageEntryDto> Packages { get; init; } = [];
}

public sealed record CreatePackageFeedRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public PackageFeedType FeedType { get; init; }

    [Required]
    [StringLength(500)]
    public string UpstreamUrl { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? ServiceConnectionId { get; init; }
}

public sealed record UpdatePackageFeedRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    [Required]
    [StringLength(500)]
    public string UpstreamUrl { get; init; } = string.Empty;

    public int? ServiceConnectionId { get; init; }
}

public sealed record PackageEntryDto
{
    public int Id { get; init; }
    public int PackageFeedId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime PublishedAt { get; init; }
    public DateTime LastSyncedAt { get; init; }
}

public sealed record AddPackageRequest
{
    [Required]
    [StringLength(300)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Outcome of an upstream sync run for a feed. Honest counts: an unsupported feed type or a
/// package the registry does not know are reported as such, never counted as a successful sync.</summary>
public sealed record PackageFeedSyncResultDto
{
    public int Synced { get; init; }
    public int Failed { get; init; }
    public int Unsupported { get; init; }
    public string? Message { get; init; }
}
