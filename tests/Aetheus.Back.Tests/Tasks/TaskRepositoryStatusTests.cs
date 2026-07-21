// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests;

public class TaskRepositoryStatusTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TaskRepository _repo;

    public TaskRepositoryStatusTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new TaskRepository(_db, TimeProvider.System);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTask(int id, int serverId, string name, TaskExecutionStatus status)
    {
        _db.Servers.Add(new Server { Id = serverId, Name = $"srv{serverId}", Hostname = $"s{serverId}", Status = ServerStatus.Online });
        _db.Tasks.Add(new ServerTask { Id = id, ServerId = serverId, Name = name, Command = "echo", Status = status, CreatedAt = new DateTime(2026, 6, id, 0, 0, 0, DateTimeKind.Utc) });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetTasksByStatusesPagedAsync_FiltersByStatus()
    {
        SeedTask(1, 1, "build", TaskExecutionStatus.Running);
        SeedTask(2, 2, "deploy", TaskExecutionStatus.Success);
        SeedTask(3, 3, "test", TaskExecutionStatus.Failed);

        var (items, total) = await _repo.GetTasksByStatusesPagedAsync(
            null, 1, 10, [TaskExecutionStatus.Running, TaskExecutionStatus.Failed], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.DoesNotContain(items, t => t.Name == "deploy");
    }

    [Fact]
    public async Task GetTasksByStatusesPagedAsync_FiltersByAccessibleServersAndSearch()
    {
        SeedTask(1, 1, "alpha-build", TaskExecutionStatus.Running);
        SeedTask(2, 2, "beta-build", TaskExecutionStatus.Running);

        var (accessible, _) = await _repo.GetTasksByStatusesPagedAsync(
            null, 1, 10, [TaskExecutionStatus.Running], accessibleServerIds: [1], ct: TestContext.Current.CancellationToken);
        Assert.Single(accessible);

        var (searched, _) = await _repo.GetTasksByStatusesPagedAsync(
            "beta", 1, 10, [TaskExecutionStatus.Running], ct: TestContext.Current.CancellationToken);
        Assert.Single(searched);
        Assert.Equal("beta-build", searched[0].Name);
    }

    [Fact]
    public async Task ClaimPendingTasksAsync_SelfUpdateIsExclusiveAndPrioritized()
    {
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "srv1",
            Hostname = "s1",
            Status = ServerStatus.Online
        });
        _db.Tasks.AddRange(
            new ServerTask
            {
                Id = 1,
                ServerId = 1,
                Name = "older normal work",
                Command = "echo",
                Status = TaskExecutionStatus.Pending,
                CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ServerTask
            {
                Id = 2,
                ServerId = 1,
                Name = "agent update",
                Command = string.Empty,
                Operation = OperationKind.AgentSelfUpdate,
                Status = TaskExecutionStatus.Pending,
                CreatedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var claimed = await _repo.ClaimPendingTasksAsync(1, take: 2, ct: TestContext.Current.CancellationToken);

        var update = Assert.Single(claimed);
        Assert.Equal(OperationKind.AgentSelfUpdate, update.Operation);
        Assert.Equal(TaskExecutionStatus.Assigned, update.Status);
        Assert.NotNull(update.AssignedAt);
        Assert.Equal(TaskExecutionStatus.Pending, (await _db.Tasks.FindAsync([1], TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task ClaimPendingTasksAsync_DoesNotRedispatchAssignedTask()
    {
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "srv1",
            Hostname = "s1",
            Status = ServerStatus.Online
        });
        _db.Tasks.Add(new ServerTask
        {
            Id = 1,
            ServerId = 1,
            Name = "already claimed",
            Command = "deploy",
            Status = TaskExecutionStatus.Assigned,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AssignedAt = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var claimed = await _repo.ClaimPendingTasksAsync(1, take: 1, ct: TestContext.Current.CancellationToken);

        Assert.Empty(claimed);
        Assert.Equal(TaskExecutionStatus.Assigned, (await _db.Tasks.FindAsync([1], TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task GetStaleAssignedTasksAsync_UsesAssignmentTimeInsteadOfCreationTime()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var repository = new TaskRepository(_db, new FakeTimeProvider(now));
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "srv1",
            Hostname = "s1",
            Status = ServerStatus.Online
        });
        _db.Tasks.AddRange(
            new ServerTask
            {
                Id = 1,
                ServerId = 1,
                Name = "fresh claim",
                Command = "build",
                Status = TaskExecutionStatus.Assigned,
                CreatedAt = now.UtcDateTime.AddHours(-1),
                AssignedAt = now.UtcDateTime.AddSeconds(-30)
            },
            new ServerTask
            {
                Id = 2,
                ServerId = 1,
                Name = "stale claim",
                Command = "build",
                Status = TaskExecutionStatus.Assigned,
                CreatedAt = now.UtcDateTime.AddHours(-1),
                AssignedAt = now.UtcDateTime.AddMinutes(-3)
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var stale = await repository.GetStaleAssignedTasksAsync(TimeSpan.FromMinutes(2), ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(stale).Id);
    }

    [Fact]
    public async Task GetStaleRunningTasksAsync_RespectsLongerTaskTimeout()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var repository = new TaskRepository(_db, new FakeTimeProvider(now));
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "srv1",
            Hostname = "s1",
            Status = ServerStatus.Online
        });
        _db.Tasks.AddRange(
            new ServerTask
            {
                Id = 1,
                ServerId = 1,
                Name = "stale default task",
                Command = "build",
                Status = TaskExecutionStatus.Running,
                StartedAt = now.UtcDateTime.AddMinutes(-31),
                TimeoutSeconds = 300
            },
            new ServerTask
            {
                Id = 2,
                ServerId = 1,
                Name = "valid long task",
                Command = "test",
                Status = TaskExecutionStatus.Running,
                StartedAt = now.UtcDateTime.AddMinutes(-31),
                TimeoutSeconds = 2100
            },
            new ServerTask
            {
                Id = 3,
                ServerId = 1,
                Name = "stale long task",
                Command = "test",
                Status = TaskExecutionStatus.Running,
                StartedAt = now.UtcDateTime.AddMinutes(-36),
                TimeoutSeconds = 2100
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var stale = await repository.GetStaleRunningTasksAsync(TimeSpan.FromMinutes(30), ct: TestContext.Current.CancellationToken);

        Assert.Equal([1, 3], stale.Select(task => task.Id).Order());
    }
}
