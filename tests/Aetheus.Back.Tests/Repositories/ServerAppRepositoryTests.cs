// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ServerApps;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ServerAppRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServerAppRepository _repo;
    private readonly int _serverId;

    public ServerAppRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ServerAppRepository(_db);

        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetByServerIdAsync_ReturnsOrderedApps()
    {
        _db.ServerApps.AddRange(
            new ServerApp { ServerId = _serverId, Name = "Zeta", Source = "manual" },
            new ServerApp { ServerId = _serverId, Name = "Alpha", Source = "manual" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByServerIdAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsSecondPageAndTotal()
    {
        _db.ServerApps.AddRange(Enumerable.Range(0, 205).Select(index => new ServerApp
        {
            ServerId = _serverId,
            Name = $"App-{index:D3}",
            Source = "manual"
        }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetPageAsync(
            _serverId, null, "Name", false, 2, 200, ct: TestContext.Current.CancellationToken);

        Assert.Equal(205, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("App-200", items[0].Name);
    }

    [Fact]
    public async Task GetPageAsync_SearchFiltersVersionAndSource()
    {
        _db.ServerApps.AddRange(
            new ServerApp { ServerId = _serverId, Name = "Web", Version = "needle-version", Source = "manual" },
            new ServerApp { ServerId = _serverId, Name = "Worker", Version = "1.0", Source = "needle-source" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (versionMatches, _) = await _repo.GetPageAsync(
            _serverId, "needle-version", null, false, 1, 25, ct: TestContext.Current.CancellationToken);
        var (sourceMatches, _) = await _repo.GetPageAsync(
            _serverId, "needle-source", null, false, 1, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal("Web", Assert.Single(versionMatches).Name);
        Assert.Equal("Worker", Assert.Single(sourceMatches).Name);
    }

    [Fact]
    public async Task GetByServerIdAsync_FiltersbyServerId()
    {
        var other = new Server { Name = "other", Hostname = "h2" };
        _db.Servers.Add(other);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServerApps.AddRange(
            new ServerApp { ServerId = _serverId, Name = "App1", Source = "manual" },
            new ServerApp { ServerId = other.Id, Name = "App2", Source = "manual" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByServerIdAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetByIdAsync_Found()
    {
        var app = new ServerApp { ServerId = _serverId, Name = "App", Source = "manual" };
        _db.ServerApps.Add(app);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByIdAsync(app.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_PersistsAndReturns()
    {
        var app = new ServerApp { ServerId = _serverId, Name = "New", Source = "manual" };
        var result = await _repo.AddAsync(app, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Id > 0);
        Assert.Equal(1, await _db.ServerApps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var app = new ServerApp { ServerId = _serverId, Name = "Del", Source = "manual" };
        _db.ServerApps.Add(app);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(app, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.ServerApps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.ServerApps.Add(new ServerApp { ServerId = _serverId, Name = "Pending", Source = "manual" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ServerApps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
