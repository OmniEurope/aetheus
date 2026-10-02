// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Plugins;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class PluginRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PluginRepository _repo;

    public PluginRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PluginRepository(_db);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByName()
    {
        _db.PluginRegistrations.AddRange(
            new PluginRegistration { Name = "Zeta", Version = "1.0", Type = PluginType.Collector },
            new PluginRegistration { Name = "Alpha", Version = "1.0", Type = PluginType.Executor }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal("Zeta", result[1].Name);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsSecondPageWithStableSort()
    {
        _db.PluginRegistrations.AddRange(Enumerable.Range(0, 205).Select(index =>
            new PluginRegistration
            {
                Name = $"Plugin-{index:D3}",
                Version = "1.0",
                Type = PluginType.Collector
            }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetPageAsync(null, "Name", false, 2, 200, ct: TestContext.Current.CancellationToken);

        Assert.Equal(205, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("Plugin-200", items[0].Name);
    }

    [Fact]
    public async Task GetPageAsync_SearchFiltersNameVersionAndAuthor()
    {
        _db.PluginRegistrations.AddRange(
            new PluginRegistration { Name = "Metrics", Version = "1.0", Author = "Aetheus", Type = PluginType.Collector },
            new PluginRegistration { Name = "Deploy", Version = "needle-version", Author = "Ops", Type = PluginType.Executor },
            new PluginRegistration { Name = "Audit", Version = "2.0", Author = "needle-author", Type = PluginType.Collector });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (versionMatches, versionCount) = await _repo.GetPageAsync("needle-version", null, false, 1, 25, ct: TestContext.Current.CancellationToken);
        var (authorMatches, authorCount) = await _repo.GetPageAsync("needle-author", null, false, 1, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, versionCount);
        Assert.Equal("Deploy", Assert.Single(versionMatches).Name);
        Assert.Equal(1, authorCount);
        Assert.Equal("Audit", Assert.Single(authorMatches).Name);
    }

    [Fact]
    public async Task FindAsync_Found()
    {
        var plugin = new PluginRegistration { Name = "Test", Version = "1.0", Type = PluginType.Collector };
        _db.PluginRegistrations.Add(plugin);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindAsync(plugin.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task FindAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByNameVersionAsync_Found()
    {
        _db.PluginRegistrations.Add(new PluginRegistration { Name = "MyPlugin", Version = "2.0", Type = PluginType.Executor });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByNameVersionAsync("MyPlugin", "2.0", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByNameVersionAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByNameVersionAsync("Missing", "1.0", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new PluginRegistration { Name = "New", Version = "1.0", Type = PluginType.Collector }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PluginRegistrations.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var plugin = new PluginRegistration { Name = "Del", Version = "1.0", Type = PluginType.Collector };
        _db.PluginRegistrations.Add(plugin);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(plugin, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.PluginRegistrations.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.PluginRegistrations.Add(new PluginRegistration { Name = "Pending", Version = "1.0", Type = PluginType.Collector });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PluginRegistrations.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
