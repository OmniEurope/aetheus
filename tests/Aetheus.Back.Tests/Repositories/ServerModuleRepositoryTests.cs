// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ServerModules;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ServerModuleRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServerModuleRepository _repo;
    private readonly int _serverId;

    public ServerModuleRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ServerModuleRepository(_db);

        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetByServerIdAsync_ReturnsOrderedModules()
    {
        _db.ServerModules.AddRange(
            new ServerModule { ServerId = _serverId, Name = "Zeta", Type = ServerModuleType.Docker },
            new ServerModule { ServerId = _serverId, Name = "Alpha", Type = ServerModuleType.Apache }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByServerIdAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetByServerIdAsync_FiltersByServerId()
    {
        var other = new Server { Name = "other", Hostname = "h2" };
        _db.Servers.Add(other);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServerModules.AddRange(
            new ServerModule { ServerId = _serverId, Name = "Mod1", Type = ServerModuleType.Docker },
            new ServerModule { ServerId = other.Id, Name = "Mod2", Type = ServerModuleType.Apache }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByServerIdAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetByIdAsync_Found()
    {
        var mod = new ServerModule { ServerId = _serverId, Name = "Mod", Type = ServerModuleType.Docker };
        _db.ServerModules.Add(mod);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByIdAsync(mod.Id, ct: TestContext.Current.CancellationToken);
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
        var mod = new ServerModule { ServerId = _serverId, Name = "New", Type = ServerModuleType.Docker };
        var result = await _repo.AddAsync(mod, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Id > 0);
        Assert.Equal(1, await _db.ServerModules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var mod = new ServerModule { ServerId = _serverId, Name = "Del", Type = ServerModuleType.Docker };
        _db.ServerModules.Add(mod);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(mod, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.ServerModules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.ServerModules.Add(new ServerModule { ServerId = _serverId, Name = "Pending", Type = ServerModuleType.Docker });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ServerModules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
