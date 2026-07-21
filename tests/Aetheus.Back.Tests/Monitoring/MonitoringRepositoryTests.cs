// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Monitoring;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class MonitoringRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MonitoringRepository _repo;

    public MonitoringRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new MonitoringRepository(_db);
    }

    [Fact]
    public async Task GetAllServersAsync_ReturnsAll()
    {
        _db.Servers.AddRange(
            new Server { Name = "s1", Hostname = "h1" },
            new Server { Name = "s2", Hostname = "h2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllServersAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CountPendingTasksAsync_CountsCorrectly()
    {
        var server = new Server { Name = "s", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Tasks.AddRange(
            new ServerTask { ServerId = server.Id, Name = "t1", Command = "c", Status = TaskExecutionStatus.Pending },
            new ServerTask { ServerId = server.Id, Name = "t2", Command = "c", Status = TaskExecutionStatus.Running },
            new ServerTask { ServerId = server.Id, Name = "t3", Command = "c", Status = TaskExecutionStatus.Pending }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var count = await _repo.CountPendingTasksAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountRunningPipelinesAsync_CountsCorrectly()
    {
        var p = new Pipeline { Name = "p", YamlDefinition = "y" };
        _db.Pipelines.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PipelineRuns.AddRange(
            new PipelineRun { PipelineId = p.Id, Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow },
            new PipelineRun { PipelineId = p.Id, Status = PipelineStatus.Success, StartedAt = DateTime.UtcNow },
            new PipelineRun { PipelineId = p.Id, Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var count = await _repo.CountRunningPipelinesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetRecentRunsAsync_ReturnsOrderedByDate()
    {
        var p = new Pipeline { Name = "p", YamlDefinition = "y" };
        _db.Pipelines.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PipelineRuns.AddRange(
            new PipelineRun { PipelineId = p.Id, Status = PipelineStatus.Success, StartedAt = DateTime.UtcNow.AddHours(-2) },
            new PipelineRun { PipelineId = p.Id, Status = PipelineStatus.Success, StartedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRecentRunsAsync(5, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].StartedAt > result[1].StartedAt);
    }

    [Fact]
    public async Task DirectPipelineRead_IncludesOnlyThatPipelinesRunsWithoutProjectRead()
    {
        var allowed = new Pipeline { Name = "allowed", YamlDefinition = "y" };
        var denied = new Pipeline { Name = "denied", YamlDefinition = "y" };
        _db.Pipelines.AddRange(allowed, denied);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PipelineRuns.AddRange(
            new PipelineRun { PipelineId = allowed.Id, Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow },
            new PipelineRun { PipelineId = denied.Id, Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var runs = await _repo.GetRecentRunsAsync(10, [], [allowed.Id], TestContext.Current.CancellationToken);
        var count = await _repo.CountRunningPipelinesAsync([], [allowed.Id], TestContext.Current.CancellationToken);

        Assert.Single(runs);
        Assert.Equal(allowed.Id, runs[0].PipelineId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetServerMetricsSinceAsync_FiltersAndOrders()
    {
        var server = new Server { Name = "s", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var now = DateTime.UtcNow;
        _db.ServerMetrics.AddRange(
            new ServerMetric { ServerId = server.Id, Timestamp = now.AddHours(-5), CpuPercent = 10 },
            new ServerMetric { ServerId = server.Id, Timestamp = now.AddHours(-1), CpuPercent = 20 },
            new ServerMetric { ServerId = server.Id, Timestamp = now, CpuPercent = 30 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetServerMetricsSinceAsync(server.Id, now.AddHours(-2), ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal(20, result[0].CpuPercent);
        Assert.Equal(30, result[1].CpuPercent);
    }

    public void Dispose() => _db.Dispose();
}
