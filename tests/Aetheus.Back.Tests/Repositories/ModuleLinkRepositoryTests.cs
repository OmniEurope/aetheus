// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ModuleLinks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ModuleLinkRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ModuleLinkRepository _repo;
    private readonly int _serverId;

    public ModuleLinkRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ModuleLinkRepository(_db);

        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetLinksAsync_ReturnsOrderedLinks()
    {
        _db.ModuleLinks.AddRange(
            new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" },
            new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Apache, SourceIdentifier = "vh2", TargetType = ModuleLinkType.Certbot, TargetIdentifier = "cert1" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetLinksAsync(_serverId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(ModuleLinkType.Docker, result[0].SourceType);
    }

    [Fact]
    public async Task GetLinksForResourceAsync_ReturnsBidirectionalLinks()
    {
        _db.ModuleLinks.AddRange(
            new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" },
            new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Apache, SourceIdentifier = "vh1", TargetType = ModuleLinkType.Certbot, TargetIdentifier = "cert1" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetLinksForResourceAsync(_serverId, ModuleLinkType.Apache, "vh1", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetLinksPageAsync_ReturnsSecondPageAcrossResourceSet()
    {
        _db.ModuleLinks.AddRange(Enumerable.Range(0, 205).Select(index => new ModuleLink
        {
            ServerId = _serverId,
            SourceType = ModuleLinkType.Docker,
            SourceIdentifier = index % 2 == 0 ? "web" : "worker",
            TargetType = ModuleLinkType.Apache,
            TargetIdentifier = $"site-{index:D3}"
        }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetLinksPageAsync(_serverId, new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["web", "worker"],
            Page = 2,
            PageSize = 200,
            SortBy = "Identifier"
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(205, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("site-200", items[0].TargetIdentifier);
    }

    [Fact]
    public async Task GetLinksPageAsync_SearchesOnlyLinkedSide()
    {
        _db.ModuleLinks.AddRange(
            new ModuleLink
            {
                ServerId = _serverId,
                SourceType = ModuleLinkType.Docker,
                SourceIdentifier = "needle-resource",
                TargetType = ModuleLinkType.Apache,
                TargetIdentifier = "unrelated"
            },
            new ModuleLink
            {
                ServerId = _serverId,
                SourceType = ModuleLinkType.Docker,
                SourceIdentifier = "needle-resource",
                TargetType = ModuleLinkType.Apache,
                TargetIdentifier = "needle-target"
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetLinksPageAsync(_serverId, new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["needle-resource"],
            Search = "needle-target"
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, totalCount);
        Assert.Equal("needle-target", Assert.Single(items).TargetIdentifier);
    }

    [Fact]
    public async Task FindLinkAsync_Found()
    {
        var link = new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" };
        _db.ModuleLinks.Add(link);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindLinkAsync(link.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindLinkAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindLinkAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ModuleLinks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var link = new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" };
        _db.ModuleLinks.Add(link);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(link, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.ModuleLinks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkExistsAsync_Exists_ReturnsTrue()
    {
        _db.ModuleLinks.Add(new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.LinkExistsAsync(_serverId, ModuleLinkType.Docker, "web", ModuleLinkType.Apache, "vh1", ct: TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task LinkExistsAsync_NotExists_ReturnsFalse()
    {
        var result = await _repo.LinkExistsAsync(_serverId, ModuleLinkType.Docker, "web", ModuleLinkType.Apache, "vh1", ct: TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.ModuleLinks.Add(new ModuleLink { ServerId = _serverId, SourceType = ModuleLinkType.Docker, SourceIdentifier = "web", TargetType = ModuleLinkType.Apache, TargetIdentifier = "vh1" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ModuleLinks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
