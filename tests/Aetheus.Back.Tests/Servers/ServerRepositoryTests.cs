// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class ServerRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServerRepository _repo;
    private readonly ServerHeartbeatRepository _heartbeatRepo;

    public ServerRepositoryTests()
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

    // --- GetServersPagedAsync ---

    [Fact]
    public async Task GetServersPagedAsync_BasicPagination()
    {
        _db.Servers.AddRange(
            new Server { Name = "a-srv", Hostname = "a" },
            new Server { Name = "b-srv", Hostname = "b" },
            new Server { Name = "c-srv", Hostname = "c" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetServersPagedAsync(null, null, false, null, null, 1, 2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetServersPagedAsync_ByType()
    {
        _db.Servers.AddRange(
            new Server { Name = "docker1", Hostname = "d1", Type = ServerType.Docker },
            new Server { Name = "normal1", Hostname = "n1", Type = ServerType.Normal }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetServersPagedAsync(null, null, false, ServerType.Docker, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal("docker1", items[0].Name);
    }

    [Fact]
    public async Task GetServersPagedAsync_ByStatus()
    {
        _db.Servers.AddRange(
            new Server { Name = "on", Hostname = "on", Status = ServerStatus.Online },
            new Server { Name = "off", Hostname = "off", Status = ServerStatus.Offline }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetServersPagedAsync(null, null, false, null, ServerStatus.Online, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal("on", items[0].Name);
    }

    [Fact]
    public async Task GetServersPagedAsync_TextSearch()
    {
        _db.Servers.AddRange(
            new Server { Name = "web-prod", Hostname = "wp.local", Tags = "[]" },
            new Server { Name = "db-prod", Hostname = "dp.local", Tags = "[]" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetServersPagedAsync("web", null, false, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal("web-prod", items[0].Name);
    }

    [Fact]
    public async Task GetServersPagedAsync_SortByName_Descending()
    {
        _db.Servers.AddRange(
            new Server { Name = "alpha", Hostname = "a" },
            new Server { Name = "bravo", Hostname = "b" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetServersPagedAsync(null, "name", true, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal("bravo", items[0].Name);
        Assert.Equal("alpha", items[1].Name);
    }

    [Fact]
    public async Task GetServersPagedAsync_SortByStatus()
    {
        _db.Servers.AddRange(
            new Server { Name = "a", Hostname = "a", Status = ServerStatus.Online },
            new Server { Name = "b", Hostname = "b", Status = ServerStatus.Offline }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetServersPagedAsync(null, "status", false, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetServersPagedAsync_SortByLastHeartbeat()
    {
        _db.Servers.AddRange(
            new Server { Name = "a", Hostname = "a", LastHeartbeat = DateTime.UtcNow.AddHours(-2) },
            new Server { Name = "b", Hostname = "b", LastHeartbeat = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetServersPagedAsync(null, "lastheartbeat", true, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal("b", items[0].Name);
    }

    [Fact]
    public async Task GetServersPagedAsync_DefaultSort_OnlineFirstThenHeartbeatDescending()
    {
        var now = DateTime.UtcNow;
        _db.Servers.AddRange(
            // Offline servers, freshest first within the group
            new Server { Name = "off-stale", Hostname = "o1", Status = ServerStatus.Offline, LastHeartbeat = now.AddHours(-3) },
            new Server { Name = "off-fresh", Hostname = "o2", Status = ServerStatus.Offline, LastHeartbeat = now.AddMinutes(-10) },
            // Online servers must lead regardless of heartbeat recency vs. offline ones
            new Server { Name = "on-stale", Hostname = "n1", Status = ServerStatus.Online, LastHeartbeat = now.AddHours(-1) },
            new Server { Name = "on-fresh", Hostname = "n2", Status = ServerStatus.Online, LastHeartbeat = now }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // No sort column chosen → default ordering applies.
        var (items, _) = await _repo.GetServersPagedAsync(null, null, false, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(["on-fresh", "on-stale", "off-fresh", "off-stale"], items.Select(s => s.Name));
    }

    // --- GetServerDetailAsync ---

    [Fact]
    public async Task GetServerDetailAsync_Found_IncludesAllCollections()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServiceInfos.Add(new ServiceInfo { ServerId = server.Id, Name = "nginx", Type = ServiceType.Systemd, Status = "active", IsRunning = true });
        _db.DockerContainers.Add(new DockerContainer { ServerId = server.Id, ContainerId = "c1", Name = "web", Image = "nginx", State = "running", Status = "Up" });
        _db.ServerMetrics.Add(new ServerMetric { ServerId = server.Id, Timestamp = DateTime.UtcNow, CpuPercent = 50 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetServerDetailAsync(server.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Services);
        Assert.Single(result.DockerContainers);
        Assert.Single(result.Metrics);
    }

    [Fact]
    public async Task GetServerDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetServerDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    // --- FindServerAsync ---

    [Fact]
    public async Task FindServerAsync_Found()
    {
        var s = new Server { Name = "s", Hostname = "h" };
        _db.Servers.Add(s);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindServerAsync(s.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    // --- RemoveServerAsync ---

    [Fact]
    public async Task RemoveServerAsync_Removes()
    {
        var s = new Server { Name = "s", Hostname = "h" };
        _db.Servers.Add(s);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveServerAsync(s, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.Servers.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- GetServerWithCollectionsAsync ---

    [Fact]
    public async Task GetServerWithCollectionsAsync_IncludesCollections()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.DockerImages.Add(new DockerImage { ServerId = server.Id, ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "142MB" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetServerWithCollectionsAsync(server.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.DockerImages);
    }

    // --- AddMetricAsync ---

    [Fact]
    public async Task AddMetricAsync_AddsToContext()
    {
        var s = new Server { Name = "s", Hostname = "h" };
        _db.Servers.Add(s);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.AddMetricAsync(new ServerMetric { ServerId = s.Id, Timestamp = DateTime.UtcNow, CpuPercent = 50 }, ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.ServerMetrics.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- ReplaceServicesAsync ---

    [Fact]
    public async Task ReplaceServicesAsync_RemovesOldAndAddsNew()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServiceInfos.Add(new ServiceInfo { ServerId = server.Id, Name = "old", Type = ServiceType.Systemd, Status = "active", IsRunning = true });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceServicesAsync(server.Id, [new ServiceInfo { ServerId = server.Id, Name = "new", Type = ServiceType.Systemd, Status = "active", IsRunning = true }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var services = await _db.ServiceInfos.Where(s => s.ServerId == server.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(services);
        Assert.Equal("new", services[0].Name);
    }

    // --- ReplaceDockerContainersAsync ---

    [Fact]
    public async Task ReplaceDockerContainersAsync_ReplacesAll()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.DockerContainers.Add(new DockerContainer { ServerId = server.Id, ContainerId = "old", Name = "old", Image = "img", State = "running", Status = "Up" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceDockerContainersAsync(server.Id, [new DockerContainer { ServerId = server.Id, ContainerId = "new", Name = "new", Image = "img", State = "running", Status = "Up" }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var containers = await _db.DockerContainers.Where(c => c.ServerId == server.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(containers);
        Assert.Equal("new", containers[0].ContainerId);
    }

    // --- ReplaceDockerImagesAsync ---

    [Fact]
    public async Task ReplaceDockerImagesAsync_ReplacesAll()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceDockerImagesAsync(server.Id, [new DockerImage { ServerId = server.Id, ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "100MB" }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.DockerImages.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- ReplaceDockerComposeStacksAsync ---

    [Fact]
    public async Task ReplaceDockerComposeStacksAsync_ReplacesAll()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceDockerComposeStacksAsync(server.Id, [new DockerComposeStack { ServerId = server.Id, Name = "stack1", Status = "running", ConfigFile = "/c.yml" }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.DockerComposeStacks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- ReplaceDockerNetworksAsync ---

    [Fact]
    public async Task ReplaceDockerNetworksAsync_ReplacesAll()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceDockerNetworksAsync(server.Id, [new DockerNetwork { ServerId = server.Id, NetworkId = "n1", Name = "bridge", Driver = "bridge", Scope = "local" }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.DockerNetworks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- ReplaceDockerVolumesAsync ---

    [Fact]
    public async Task ReplaceDockerVolumesAsync_ReplacesAll()
    {
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _heartbeatRepo.ReplaceDockerVolumesAsync(server.Id, [new DockerVolume { ServerId = server.Id, Name = "vol1", Driver = "local", Mountpoint = "/data" }], ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.DockerVolumes.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- GetStaleOnlineServersAsync ---

    [Fact]
    public async Task GetStaleOnlineServersAsync_ReturnsStaleOnly()
    {
        _db.Servers.AddRange(
            new Server { Name = "stale", Hostname = "h1", Status = ServerStatus.Online, LastHeartbeat = DateTime.UtcNow.AddMinutes(-10) },
            new Server { Name = "fresh", Hostname = "h2", Status = ServerStatus.Online, LastHeartbeat = DateTime.UtcNow },
            new Server { Name = "offline", Hostname = "h3", Status = ServerStatus.Offline, LastHeartbeat = DateTime.UtcNow.AddMinutes(-10) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetStaleOnlineServersAsync(TimeSpan.FromMinutes(5), ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("stale", result[0].Name);
    }

    [Fact]
    public async Task TryMarkOfflineIfStaleAsync_ObservedHeartbeatUnchanged_MarksOffline()
    {
        var observed = DateTime.UtcNow.AddMinutes(-10);
        var server = new Server
        {
            Name = "stale",
            Hostname = "stale",
            Status = ServerStatus.Online,
            LastHeartbeat = observed
        };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var changed = await _repo.TryMarkOfflineIfStaleAsync(
            server.Id, observed, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.Equal(ServerStatus.Offline, server.Status);
    }

    [Fact]
    public async Task TryMarkOfflineIfStaleAsync_HeartbeatChangedAfterObservation_PreservesOnline()
    {
        var observed = DateTime.UtcNow.AddMinutes(-10);
        var server = new Server
        {
            Name = "refreshed",
            Hostname = "refreshed",
            Status = ServerStatus.Online,
            LastHeartbeat = observed
        };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        server.LastHeartbeat = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var changed = await _repo.TryMarkOfflineIfStaleAsync(
            server.Id, observed, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.False(changed);
        Assert.Equal(ServerStatus.Online, server.Status);
    }

    // --- SaveChangesAsync ---

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.Servers.Add(new Server { Name = "s", Hostname = "h" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Servers.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
