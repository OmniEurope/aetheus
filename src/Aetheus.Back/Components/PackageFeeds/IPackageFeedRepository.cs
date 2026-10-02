// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.PackageFeeds;

public interface IPackageFeedRepository
{
    // Projected list view: summary columns + a package COUNT subquery (no Package entities loaded).
    Task<(List<PackageFeedDto> Items, int Total)> GetPagedFeedsAsync(
        int? projectId, string? search, int page, int pageSize,
        string? sortBy, bool sortDescending, CancellationToken ct = default,
        IReadOnlyList<GridFilter>? columnFilters = null);
    Task<PackageFeed?> GetFeedDetailAsync(int id, CancellationToken ct = default);
    Task<PackageFeed?> FindFeedAsync(int id, CancellationToken ct = default);
    Task AddFeedAsync(PackageFeed feed, CancellationToken ct = default);
    Task RemoveFeedAsync(PackageFeed feed, CancellationToken ct = default);
    Task<PackageEntry?> FindPackageByNameAsync(int feedId, string name, CancellationToken ct = default);
    Task<PackageEntry?> FindPackageAsync(int packageId, CancellationToken ct = default);
    Task<List<PackageEntry>> GetTrackedPackagesAsync(int feedId, CancellationToken ct = default);
    Task<List<int>> GetFeedIdsAsync(CancellationToken ct = default);
    Task AddPackageAsync(PackageEntry entry, CancellationToken ct = default);
    Task RemovePackageAsync(PackageEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
