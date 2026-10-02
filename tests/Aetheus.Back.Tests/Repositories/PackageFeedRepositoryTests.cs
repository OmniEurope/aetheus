// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.PackageFeeds;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class PackageFeedRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PackageFeedRepository _repo;

    public PackageFeedRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PackageFeedRepository(_db);
    }

    [Fact]
    public async Task GetPagedFeedsAsync_ReturnsPage_WithProjectedPackageCount()
    {
        var nuget = new PackageFeed { Name = "NuGet", FeedType = PackageFeedType.NuGet, UpstreamUrl = "https://nuget.org" };
        _db.PackageFeeds.AddRange(
            nuget,
            new PackageFeed { Name = "npm", FeedType = PackageFeedType.Npm, UpstreamUrl = "https://npmjs.com" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PackageEntries.AddRange(
            new PackageEntry { PackageFeedId = nuget.Id, Name = "Newtonsoft.Json" },
            new PackageEntry { PackageFeedId = nuget.Id, Name = "xunit" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (result, total) = await _repo.GetPagedFeedsAsync(null, null, 1, 10, null, false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, total);
        Assert.Equal(2, result.Count);
        Assert.Equal("npm", result[0].Name);
        Assert.Equal("NuGet", result[1].Name);
        // The projected PackageCount comes from a COUNT, not from materialized Package entities.
        Assert.Equal(2, result[1].PackageCount);
        Assert.Equal(0, result[0].PackageCount);
    }

    [Fact]
    public async Task GetPagedFeedsAsync_WithProjectId_Filters()
    {
        var p = new Project { Name = "P1" };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PackageFeeds.AddRange(
            new PackageFeed { Name = "Global", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u", ProjectId = null },
            new PackageFeed { Name = "Project", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u", ProjectId = p.Id },
            new PackageFeed { Name = "Other", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u", ProjectId = 999 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (result, total) = await _repo.GetPagedFeedsAsync(p.Id, null, 1, 10, null, false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetFeedDetailAsync_Found_IncludesPackages()
    {
        var feed = new PackageFeed { Name = "Feed", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" };
        _db.PackageFeeds.Add(feed);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PackageEntries.Add(new PackageEntry { PackageFeedId = feed.Id, Name = "Pkg", LatestVersion = "1.0", PublishedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetFeedDetailAsync(feed.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Packages);
    }

    [Fact]
    public async Task GetFeedDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetFeedDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindFeedAsync_Found()
    {
        var feed = new PackageFeed { Name = "Feed", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" };
        _db.PackageFeeds.Add(feed);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindFeedAsync(feed.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddFeedAsync_Persists()
    {
        await _repo.AddFeedAsync(new PackageFeed { Name = "New", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PackageFeeds.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveFeedAsync_Removes()
    {
        var feed = new PackageFeed { Name = "Del", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" };
        _db.PackageFeeds.Add(feed);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveFeedAsync(feed, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.PackageFeeds.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindPackageByNameAsync_Found()
    {
        var feed = new PackageFeed { Name = "F", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" };
        _db.PackageFeeds.Add(feed);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PackageEntries.Add(new PackageEntry { PackageFeedId = feed.Id, Name = "MyPkg", LatestVersion = "2.0", PublishedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindPackageByNameAsync(feed.Id, "MyPkg", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("2.0", result.LatestVersion);
    }

    [Fact]
    public async Task FindPackageByNameAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindPackageByNameAsync(1, "Missing", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddPackageAsync_Persists()
    {
        var feed = new PackageFeed { Name = "F", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" };
        _db.PackageFeeds.Add(feed);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddPackageAsync(new PackageEntry { PackageFeedId = feed.Id, Name = "Pkg", LatestVersion = "1.0", PublishedAt = DateTime.UtcNow }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PackageEntries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.PackageFeeds.Add(new PackageFeed { Name = "Pending", FeedType = PackageFeedType.NuGet, UpstreamUrl = "u" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PackageFeeds.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
