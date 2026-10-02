// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Apache;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ApacheRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApacheRepository _repo;
    private readonly int _serverId;

    public ApacheRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ApacheRepository(_db);

        var server = new Server { Name = "S1", Hostname = "h1", Status = ServerStatus.Online };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetStateAsync_Found_ReturnsState()
    {
        _db.ApacheStates.Add(new ApacheState { ServerId = _serverId, IsRunning = true, Version = "2.4.54" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetStateAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.IsRunning);
    }

    [Fact]
    public async Task GetStateAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetStateAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetModulesAsync_ReturnsOrderedByName()
    {
        _db.ApacheModules.AddRange(
            new ApacheModule { ServerId = _serverId, Name = "mod_rewrite", IsEnabled = true },
            new ApacheModule { ServerId = _serverId, Name = "mod_headers", IsEnabled = true }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetModulesAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("mod_headers", result[0].Name);
    }

    [Fact]
    public async Task GetVirtualHostsAsync_ReturnsOrderedByServerName()
    {
        _db.ApacheVirtualHosts.AddRange(
            new ApacheVirtualHost { ServerId = _serverId, ServerName = "zeta.com", Port = 80, DocumentRoot = "/var/www/zeta" },
            new ApacheVirtualHost { ServerId = _serverId, ServerName = "alpha.com", Port = 80, DocumentRoot = "/var/www/alpha" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetVirtualHostsAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("alpha.com", result[0].ServerName);
    }

    [Fact]
    public async Task ServerExistsAsync_Exists_ReturnsTrue()
    {
        var result = await _repo.ServerExistsAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task ServerExistsAsync_NotExists_ReturnsFalse()
    {
        var result = await _repo.ServerExistsAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task AddTaskAsync_Persists()
    {
        await _repo.AddTaskAsync(new ServerTask { ServerId = _serverId, Name = "apache_action", Command = "restart", Status = TaskExecutionStatus.Pending }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Tasks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
