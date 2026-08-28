// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class PackageFeed
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PackageFeedType FeedType { get; set; }
    public string UpstreamUrl { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public int? ServiceConnectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public ServiceConnection? ServiceConnection { get; set; }
    public List<PackageEntry> Packages { get; set; } = [];
}

public class PackageEntry
{
    public int Id { get; set; }
    public int PackageFeedId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }

    // Navigation
    public PackageFeed PackageFeed { get; set; } = null!;
}
