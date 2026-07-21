// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Portsentry;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class PortsentryRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PortsentryRepository _repo;
    private readonly int _serverId;

    public PortsentryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PortsentryRepository(_db);

        var server = new Server { Name = "S1", Hostname = "h1", Status = ServerStatus.Online };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    // --- Whitelist ---

    [Fact]
    public async Task GetWhitelistAsync_Empty_ReturnsEmptyList()
    {
        var result = await _repo.GetWhitelistAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AddWhitelistIpAsync_Persists()
    {
        var entry = new PortsentryWhitelistIp
        {
            ServerId = _serverId,
            IpAddress = "192.168.1.1",
            Description = "Test"
        };

        var result = await _repo.AddWhitelistIpAsync(entry, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Id > 0);
        Assert.Equal(1, await _db.PortsentryWhitelistIps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWhitelistAsync_ReturnsOrderedByIp()
    {
        _db.PortsentryWhitelistIps.AddRange(
            new PortsentryWhitelistIp { ServerId = _serverId, IpAddress = "10.0.0.2", Description = "B" },
            new PortsentryWhitelistIp { ServerId = _serverId, IpAddress = "10.0.0.1", Description = "A" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetWhitelistAsync(_serverId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("10.0.0.1", result[0].IpAddress);
    }

    [Fact]
    public async Task RemoveWhitelistIpAsync_RemovesEntry()
    {
        var entry = new PortsentryWhitelistIp
        {
            ServerId = _serverId,
            IpAddress = "10.0.0.1",
            Description = "Test"
        };
        _db.PortsentryWhitelistIps.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveWhitelistIpAsync(entry.Id, _serverId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.PortsentryWhitelistIps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveWhitelistIpAsync_NonExistent_NoOp()
    {
        await _repo.RemoveWhitelistIpAsync(999, _serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.PortsentryWhitelistIps.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWhitelistAsync_DoesNotReturnOtherServer()
    {
        var otherServer = new Server { Name = "S2", Hostname = "h2", Status = ServerStatus.Online };
        _db.Servers.Add(otherServer);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PortsentryWhitelistIps.AddRange(
            new PortsentryWhitelistIp { ServerId = _serverId, IpAddress = "10.0.0.1" },
            new PortsentryWhitelistIp { ServerId = otherServer.Id, IpAddress = "10.0.0.2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetWhitelistAsync(_serverId, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("10.0.0.1", result[0].IpAddress);
    }

    [Fact]
    public async Task PagedSecurityLists_ReturnRequestedPageAndTotals()
    {
        _db.PortsentryBlockedIps.AddRange(
            new PortsentryBlockedIp { ServerId = _serverId, IpAddress = "10.0.0.2", BlockedAt = DateTime.UtcNow.AddMinutes(-1) },
            new PortsentryBlockedIp { ServerId = _serverId, IpAddress = "10.0.0.1", BlockedAt = DateTime.UtcNow });
        _db.PortsentryWhitelistIps.AddRange(
            new PortsentryWhitelistIp { ServerId = _serverId, IpAddress = "10.0.0.2" },
            new PortsentryWhitelistIp { ServerId = _serverId, IpAddress = "10.0.0.1" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (blocked, blockedTotal) = await _repo.GetBlockedIpsPagedAsync(
            _serverId, null, 1, 1, "BlockedAt", true, ct: TestContext.Current.CancellationToken);
        var (whitelist, whitelistTotal) = await _repo.GetWhitelistPagedAsync(
            _serverId, null, 2, 1, "IpAddress", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, blockedTotal);
        Assert.Single(blocked);
        Assert.Equal("10.0.0.1", blocked[0].IpAddress);
        Assert.Equal(2, whitelistTotal);
        Assert.Single(whitelist);
        Assert.Equal("10.0.0.2", whitelist[0].IpAddress);
    }

    // --- AddTaskAsync ---

    [Fact]
    public async Task AddTaskAsync_Persists()
    {
        await _repo.AddTaskAsync(new ServerTask
        {
            ServerId = _serverId,
            Name = "ps_action",
            Command = "systemctl restart portsentry",
            Status = TaskExecutionStatus.Pending
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.Tasks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
