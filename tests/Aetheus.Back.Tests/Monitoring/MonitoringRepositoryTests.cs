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
    public async Task ScopedDashboardQueries_FailClosedForEmptyListsAndCoverEveryPipelineOwnerPath()
    {
        var allowedProject = new Project { Name = "allowed" };
        var deniedProject = new Project { Name = "denied" };
        _db.Projects.AddRange(allowedProject, deniedProject);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var environment = new Aetheus.Back.Data.Entities.Environment
        {
            Name = "prod",
            ProjectId = allowedProject.Id
        };
        var projectServer = new ProjectServer
        {
            ProjectId = allowedProject.Id,
            DisplayName = "edge",
            Host = "edge.example"
        };
        _db.Environments.Add(environment);
        _db.ProjectServers.Add(projectServer);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var directProject = new Pipeline
        { Name = "project", YamlDefinition = "y", ProjectId = allowedProject.Id };
        var viaEnvironment = new Pipeline
        { Name = "environment", YamlDefinition = "y", EnvironmentId = environment.Id };
        var viaProjectServer = new Pipeline
        { Name = "project-server", YamlDefinition = "y", ProjectServerId = projectServer.Id };
        var directlyGranted = new Pipeline
        { Name = "direct-grant", YamlDefinition = "y", ProjectId = deniedProject.Id };
        var denied = new Pipeline
        { Name = "denied", YamlDefinition = "y", ProjectId = deniedProject.Id };
        _db.Pipelines.AddRange(
            directProject,
            viaEnvironment,
            viaProjectServer,
            directlyGranted,
            denied);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var pipeline in new[]
                 { directProject, viaEnvironment, viaProjectServer, directlyGranted, denied })
        {
            _db.PipelineRuns.Add(new PipelineRun
            {
                PipelineId = pipeline.Id,
                Status = PipelineStatus.Running,
                StartedAt = DateTime.UtcNow
            });
        }

        var allowedServer = new Server { Name = "allowed", Hostname = "allowed" };
        var deniedServer = new Server { Name = "denied", Hostname = "denied" };
        _db.Servers.AddRange(allowedServer, deniedServer);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Tasks.AddRange(
            new ServerTask
            {
                ServerId = allowedServer.Id,
                Name = "allowed",
                Command = "c",
                Status = TaskExecutionStatus.Pending
            },
            new ServerTask
            {
                ServerId = deniedServer.Id,
                Name = "denied",
                Command = "c",
                Status = TaskExecutionStatus.Pending
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await _repo.GetAllServersAsync([], TestContext.Current.CancellationToken));
        Assert.Equal(0, await _repo.CountPendingTasksAsync([], TestContext.Current.CancellationToken));
        Assert.Equal(
            [allowedServer.Id],
            (await _repo.GetAllServersAsync(
                [allowedServer.Id],
                TestContext.Current.CancellationToken)).Select(server => server.Id));
        Assert.Equal(
            1,
            await _repo.CountPendingTasksAsync(
                [allowedServer.Id],
                TestContext.Current.CancellationToken));

        Assert.Empty(await _repo.GetRecentProjectsAsync(
            10, [], TestContext.Current.CancellationToken));
        Assert.Equal(
            [allowedProject.Id],
            (await _repo.GetRecentProjectsAsync(
                10,
                [allowedProject.Id],
                TestContext.Current.CancellationToken)).Select(project => project.Id));

        var noAccess = await _repo.GetRecentRunsAsync(
            10, [], [], TestContext.Current.CancellationToken);
        Assert.Empty(noAccess);
        Assert.Equal(
            0,
            await _repo.CountRunningPipelinesAsync(
                [], [], TestContext.Current.CancellationToken));

        var projectAccess = await _repo.GetRecentRunsAsync(
            10,
            [allowedProject.Id],
            [],
            TestContext.Current.CancellationToken);
        Assert.Equal(
            [directProject.Id, viaEnvironment.Id, viaProjectServer.Id],
            projectAccess.Select(run => run.PipelineId).Order());

        var combinedAccess = await _repo.GetRecentRunsAsync(
            10,
            [allowedProject.Id],
            [directlyGranted.Id],
            TestContext.Current.CancellationToken);
        Assert.Equal(
            [directProject.Id, viaEnvironment.Id, viaProjectServer.Id, directlyGranted.Id],
            combinedAccess.Select(run => run.PipelineId).Order());
        Assert.Equal(
            4,
            await _repo.CountRunningPipelinesAsync(
                [allowedProject.Id],
                [directlyGranted.Id],
                TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task GetServerMetricsSinceAsync_AfterCursorAndTakeReturnBoundedLatestWindow()
    {
        var server = new Server { Name = "bounded", Hostname = "bounded-host" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var now = DateTime.UtcNow;
        _db.ServerMetrics.AddRange(
            Enumerable.Range(0, 5).Select(index => new ServerMetric
            {
                ServerId = server.Id,
                Timestamp = now.AddMinutes(index),
                CpuPercent = index
            }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetServerMetricsSinceAsync(
            server.Id,
            now.AddHours(-1),
            TestContext.Current.CancellationToken,
            afterUtc: now,
            take: 2);

        Assert.Equal([3, 4], result.Select(metric => metric.CpuPercent));
    }

    public void Dispose() => _db.Dispose();
}
