// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.PackageFeeds;

public class PackageFeedService(
    IPackageFeedRepository repo,
    IAuditService audit,
    IPackageVersionResolver resolver,
    PackageFeedSyncGate syncGate,
    TimeProvider timeProvider,
    IAdminChangeNotifier notifier) : IPackageFeedService
{
    public async Task<PaginatedResult<PackageFeedDto>> GetFeedsAsync(
        int? projectId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, total) = await repo.GetPagedFeedsAsync(
            projectId, request.Search, page, pageSize,
            request.SortBy, request.SortDescending, ct, request.Filters).ConfigureAwait(false);
        return new PaginatedResult<PackageFeedDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PackageFeedDetailDto?> GetFeedDetailAsync(int id, CancellationToken ct = default)
    {
        var feed = await repo.GetFeedDetailAsync(id, ct).ConfigureAwait(false);
        if (feed is null) return null;

        return new PackageFeedDetailDto
        {
            Id = feed.Id,
            Name = feed.Name,
            Description = feed.Description,
            FeedType = feed.FeedType,
            UpstreamUrl = feed.UpstreamUrl,
            ProjectId = feed.ProjectId,
            ProjectName = feed.Project?.Name,
            ServiceConnectionId = feed.ServiceConnectionId,
            ServiceConnectionName = feed.ServiceConnection?.Name,
            Packages = feed.Packages.Select(p => new PackageEntryDto
            {
                Id = p.Id,
                PackageFeedId = p.PackageFeedId,
                Name = p.Name,
                LatestVersion = p.LatestVersion,
                Description = p.Description,
                PublishedAt = p.PublishedAt,
                LastSyncedAt = p.LastSyncedAt
            }).ToList(),
            CreatedAt = feed.CreatedAt
        };
    }

    public async Task<PackageFeedDto> CreateFeedAsync(CreatePackageFeedRequest request, CancellationToken ct = default)
    {
        var entity = new PackageFeed
        {
            Name = request.Name,
            Description = request.Description,
            FeedType = request.FeedType,
            UpstreamUrl = request.UpstreamUrl,
            ProjectId = request.ProjectId,
            ServiceConnectionId = request.ServiceConnectionId
        };

        await repo.AddFeedAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "PackageFeed", entity.Id, $"{request.FeedType}: {request.Name}", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            AdminEntities.PackageFeed, entity.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<PackageFeedDto?> UpdateFeedAsync(int id, UpdatePackageFeedRequest request, CancellationToken ct = default)
    {
        var entity = await repo.FindFeedAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.UpstreamUrl = request.UpstreamUrl;
        entity.ServiceConnectionId = request.ServiceConnectionId;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "PackageFeed", id, null, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            AdminEntities.PackageFeed, id, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteFeedAsync(int id, CancellationToken ct = default)
    {
        var entity = await repo.FindFeedAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return false;

        await repo.RemoveFeedAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "PackageFeed", id, null, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            AdminEntities.PackageFeed, id, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PackageEntryDto?> AddPackageAsync(int feedId, AddPackageRequest request, CancellationToken ct = default)
    {
        var feed = await repo.FindFeedAsync(feedId, ct).ConfigureAwait(false);
        if (feed is null) return null;

        var existing = await repo.FindPackageByNameAsync(feedId, request.Name, ct).ConfigureAwait(false);
        if (existing is not null) return MapEntry(existing); // idempotent

        var entry = new PackageEntry
        {
            PackageFeedId = feedId,
            Name = request.Name,
            LatestVersion = string.Empty // unknown until the first sync - never fabricated
        };
        await repo.AddPackageAsync(entry, ct).ConfigureAwait(false);

        // Resolve immediately so the UI shows a real version (or an honest blank) right away.
        var result = await resolver.ResolveLatestAsync(feed.FeedType, feed.UpstreamUrl, entry.Name, ct).ConfigureAwait(false);
        if (result.Outcome == PackageResolveOutcome.Resolved && result.LatestVersion is not null)
        {
            entry.LatestVersion = result.LatestVersion;
            if (result.PublishedAt is { } pub) entry.PublishedAt = pub;
            entry.LastSyncedAt = timeProvider.GetUtcNow().UtcDateTime;
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await audit.LogAsync("Created", "PackageEntry", entry.Id, $"{feed.Name}/{entry.Name}", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            AdminEntities.PackageFeed, feedId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return MapEntry(entry);
    }

    public async Task<bool> RemovePackageAsync(int feedId, int packageId, CancellationToken ct = default)
    {
        var entry = await repo.FindPackageAsync(packageId, ct).ConfigureAwait(false);
        if (entry is null || entry.PackageFeedId != feedId) return false;

        await repo.RemovePackageAsync(entry, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "PackageEntry", packageId, null, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            AdminEntities.PackageFeed, feedId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PackageFeedSyncResultDto?> SyncFeedAsync(int feedId, CancellationToken ct = default)
    {
        var feed = await repo.FindFeedAsync(feedId, ct).ConfigureAwait(false);
        if (feed is null) return null;

        // Serialize per feed across scopes (hosted timer vs manual "sync now"): skip - never duplicate -
        // a sync already in flight, so the same feed cannot fan out twice and last-write-wins its versions.
        if (!await syncGate.TryEnterAsync(feedId, ct).ConfigureAwait(false))
            return new PackageFeedSyncResultDto { Synced = 0, Failed = 0, Unsupported = 0, Message = "A sync is already in progress for this feed." };

        try
        {
            var clockNow = timeProvider.GetUtcNow();
            if (syncGate.TryGetDeferral(feedId, clockNow, out var deferredUntil))
            {
                return new PackageFeedSyncResultDto
                {
                    Synced = 0,
                    Failed = 0,
                    Unsupported = 0,
                    Message = $"Registry rate limit is active; retry after {deferredUntil:O}."
                };
            }
            var packages = await repo.GetTrackedPackagesAsync(feedId, ct).ConfigureAwait(false);
            var outcome = await SyncPackagesAsync(
                feed, packages, feedId, clockNow.UtcDateTime, ct).ConfigureAwait(false);
            if (outcome.Synced > 0) await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            await audit.LogAsync(
                "Synced", "PackageFeed", feedId,
                $"synced={outcome.Synced} failed={outcome.Failed} unsupported={outcome.Unsupported}",
                ct).ConfigureAwait(false);
            if (outcome.Synced > 0)
                await notifier.BroadcastAsync(
                    AdminEntities.PackageFeed, feedId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
            return BuildSyncResult(feed, outcome);
        }
        finally
        {
            syncGate.Release(feedId);
        }
    }

    private async Task<PackageSyncOutcome> SyncPackagesAsync(
        PackageFeed feed,
        IReadOnlyCollection<PackageEntry> packages,
        int feedId,
        DateTime now,
        CancellationToken ct)
    {
        var outcome = new PackageSyncOutcome();
        foreach (var package in packages)
        {
            var result = await resolver.ResolveLatestAsync(
                feed.FeedType, feed.UpstreamUrl, package.Name, ct).ConfigureAwait(false);
            ApplyPackageResolution(package, result, outcome, feedId, now);
            if (outcome.ThrottledUntil is not null) break;
        }
        return outcome;
    }

    private void ApplyPackageResolution(
        PackageEntry package,
        PackageResolveResult result,
        PackageSyncOutcome outcome,
        int feedId,
        DateTime now)
    {
        switch (result.Outcome)
        {
            case PackageResolveOutcome.Resolved when result.LatestVersion is not null:
                package.LatestVersion = result.LatestVersion;
                if (result.PublishedAt is { } publishedAt) package.PublishedAt = publishedAt;
                package.LastSyncedAt = now;
                outcome.Synced++;
                break;
            case PackageResolveOutcome.Unsupported:
                outcome.Unsupported++;
                break;
            case PackageResolveOutcome.RateLimited:
                outcome.Failed++;
                var retryAfter = result.RetryAfter ?? TimeSpan.FromMinutes(1);
                retryAfter = TimeSpan.FromSeconds(Math.Clamp(
                    retryAfter.TotalSeconds, 1, TimeSpan.FromHours(1).TotalSeconds));
                outcome.ThrottledUntil = timeProvider.GetUtcNow().Add(retryAfter);
                syncGate.DeferUntil(feedId, outcome.ThrottledUntil.Value);
                break;
            default:
                outcome.Failed++;
                break;
        }
    }

    private static PackageFeedSyncResultDto BuildSyncResult(
        PackageFeed feed, PackageSyncOutcome outcome)
    {
        var message = outcome.ThrottledUntil is not null
            ? $"Registry rate limit reached; remaining packages were skipped until {outcome.ThrottledUntil:O}."
            : outcome.Unsupported > 0 && outcome.Synced == 0 && outcome.Failed == 0
                ? $"Feed type {feed.FeedType} is not supported for automatic version resolution yet."
                : null;
        return new PackageFeedSyncResultDto
        {
            Synced = outcome.Synced,
            Failed = outcome.Failed,
            Unsupported = outcome.Unsupported,
            Message = message
        };
    }

    private sealed class PackageSyncOutcome
    {
        public int Synced { get; set; }
        public int Failed { get; set; }
        public int Unsupported { get; set; }
        public DateTimeOffset? ThrottledUntil { get; set; }
    }

    private static PackageEntryDto MapEntry(PackageEntry p) => new()
    {
        Id = p.Id,
        PackageFeedId = p.PackageFeedId,
        Name = p.Name,
        LatestVersion = p.LatestVersion,
        Description = p.Description,
        PublishedAt = p.PublishedAt,
        LastSyncedAt = p.LastSyncedAt
    };

    private static PackageFeedDto MapToDto(PackageFeed f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        Description = f.Description,
        FeedType = f.FeedType,
        UpstreamUrl = f.UpstreamUrl,
        ProjectId = f.ProjectId,
        ProjectName = f.Project?.Name,
        ServiceConnectionName = f.ServiceConnection?.Name,
        PackageCount = f.Packages.Count,
        CreatedAt = f.CreatedAt
    };
}
