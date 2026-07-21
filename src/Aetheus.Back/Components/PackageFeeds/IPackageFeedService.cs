// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.PackageFeeds;

public interface IPackageFeedService
{
    Task<PaginatedResult<PackageFeedDto>> GetFeedsAsync(
        int? projectId, PaginationRequest request, CancellationToken ct = default);
    Task<PackageFeedDetailDto?> GetFeedDetailAsync(int id, CancellationToken ct = default);
    Task<PackageFeedDto> CreateFeedAsync(CreatePackageFeedRequest request, CancellationToken ct = default);
    Task<PackageFeedDto?> UpdateFeedAsync(int id, UpdatePackageFeedRequest request, CancellationToken ct = default);
    Task<bool> DeleteFeedAsync(int id, CancellationToken ct = default);
    Task<PackageEntryDto?> AddPackageAsync(int feedId, AddPackageRequest request, CancellationToken ct = default);
    Task<bool> RemovePackageAsync(int feedId, int packageId, CancellationToken ct = default);
    Task<PackageFeedSyncResultDto?> SyncFeedAsync(int feedId, CancellationToken ct = default);
}
