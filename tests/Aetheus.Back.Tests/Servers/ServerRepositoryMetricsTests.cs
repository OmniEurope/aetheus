// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Servers;

public class ServerRepositoryMetricsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServerRepository _repo;
    private readonly ServerHeartbeatRepository _heartbeatRepo;

    public ServerRepositoryMetricsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ServerRepository(_db, TimeProvider.System);
        // Same AppDbContext instance, deliberately: that shared context is what keeps one heartbeat
        // one transaction after the split, so the tests must exercise the pair the same way.
        _heartbeatRepo = new ServerHeartbeatRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private void SeedServer(int id, string name)
    {
        _db.Servers.Add(new Server { Id = id, Name = name, Hostname = $"{name}.local", Status = ServerStatus.Online });
        _db.SaveChanges();
    }

    private void SeedMetric(int serverId, DateTime ts)
    {
        _db.ServerMetrics.Add(new ServerMetric { ServerId = serverId, Timestamp = ts });
        _db.SaveChanges();
    }

    [Fact]
    public async Task DeleteMetricsOlderThanAsync_InMemory_RemovesExpiredAndReturnsCount()
    {
        SeedMetric(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedMetric(1, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        SeedMetric(1, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var deleted = await _heartbeatRepo.DeleteMetricsOlderThanAsync(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, deleted);
        Assert.Equal(1, await _db.ServerMetrics.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetServerIdNamePairsAsync_NoFilter_ReturnsAllOrderedByName()
    {
        SeedServer(1, "zeta");
        SeedServer(2, "alpha");

        var pairs = await _repo.GetServerIdNamePairsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("alpha", pairs[0].Name);
        Assert.Equal("zeta", pairs[1].Name);
    }

    [Fact]
    public async Task GetServerIdNamePairsAsync_WithAccessibleFilter_ReturnsOnlyAccessible()
    {
        SeedServer(1, "alpha");
        SeedServer(2, "beta");
        SeedServer(3, "gamma");

        var pairs = await _repo.GetServerIdNamePairsAsync([1, 3], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, pairs.Count);
        Assert.DoesNotContain(pairs, p => p.Name == "beta");
    }

    [Fact]
    public async Task GetRecentMetricTimestampsAsync_ReturnsTimestampsForServerSinceOrdered()
    {
        SeedMetric(1, new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));
        SeedMetric(1, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedMetric(1, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)); // before 'since'
        SeedMetric(2, new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)); // other server

        var since = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var stamps = await _heartbeatRepo.GetRecentMetricTimestampsAsync(1, since, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, stamps.Count);
        Assert.True(stamps[0] < stamps[1]);
    }
}
