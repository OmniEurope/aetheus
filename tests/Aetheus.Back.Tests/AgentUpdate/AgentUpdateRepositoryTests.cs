// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.AgentUpdate;

public sealed class AgentUpdateRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryQueueAsync_PendingWork_DoesNotDeadlockSelfUpdate()
    {
        await using var db = CreateDb();
        var request = AddReservedRequest(db);
        db.Tasks.Add(new ServerTask
        {
            ServerId = request.ServerId,
            Name = "Collect Artifacts",
            Operation = OperationKind.PipelineCollectArtifacts,
            Status = TaskExecutionStatus.Pending,
            CreatedAt = Now.UtcDateTime.AddMinutes(-1)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new AgentUpdateRepository(db, new FakeTimeProvider(Now));
        var result = await repository.TryQueueAsync(request.Id, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.BlockingTaskCount);
        Assert.NotNull(result.CreatedTask);
        Assert.Equal(OperationKind.AgentSelfUpdate, result.CreatedTask.Operation);
        Assert.Equal(TaskExecutionStatus.Pending, result.CreatedTask.Status);
        Assert.Equal(AgentUpdateRequestStatus.Queued, result.Request.Status);
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Assigned)]
    [InlineData(TaskExecutionStatus.Running)]
    public async Task TryQueueAsync_ActiveWork_StillWaitsForIdle(TaskExecutionStatus status)
    {
        await using var db = CreateDb();
        var request = AddReservedRequest(db);
        db.Tasks.Add(new ServerTask
        {
            ServerId = request.ServerId,
            Name = "Active pipeline work",
            Status = status,
            CreatedAt = Now.UtcDateTime.AddMinutes(-1)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new AgentUpdateRepository(db, new FakeTimeProvider(Now));
        var result = await repository.TryQueueAsync(request.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.BlockingTaskCount);
        Assert.Null(result.CreatedTask);
        Assert.Equal(AgentUpdateRequestStatus.WaitingForIdle, result.Request.Status);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FakeTimeProvider(Now));
    }

    private static AgentUpdateRequest AddReservedRequest(AppDbContext db)
    {
        var server = new Server
        {
            Id = 1,
            Name = "runner",
            Hostname = "runner",
            AgentUpdateReserved = true,
            AgentUpdateReservedAt = Now.UtcDateTime
        };
        var request = new AgentUpdateRequest
        {
            Id = 10,
            ServerId = server.Id,
            Server = server,
            TargetVersion = "1.0.926",
            RequestedBy = "test",
            RequestedAt = Now.UtcDateTime,
            Status = AgentUpdateRequestStatus.WaitingForIdle,
            IsActive = true
        };
        db.Servers.Add(server);
        db.AgentUpdateRequests.Add(request);
        return request;
    }

    /// <summary>
    /// Relocated with the query from ServerRepository, which had no consumer for it outside the
    /// agent-update flow. Two things matter and are asserted: the filter actually narrows to the
    /// accessible ids, and the result is ordered by name - the update view lists a fleet, and an
    /// unordered fleet is unusable.
    /// </summary>
    [Fact]
    public async Task GetServersForAgentUpdateAsync_LoadsFilteredFleetInOneOrderedResult()
    {
        await using var db = CreateDb();
        db.Servers.Add(new Server { Id = 1, Name = "zeta", Hostname = "zeta.local", Status = ServerStatus.Online });
        db.Servers.Add(new Server { Id = 2, Name = "beta", Hostname = "beta.local", Status = ServerStatus.Online });
        db.Servers.Add(new Server { Id = 3, Name = "alpha", Hostname = "alpha.local", Status = ServerStatus.Online });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, new FakeTimeProvider(Now));

        var servers = await repository.GetServersForAgentUpdateAsync(
            [1, 3], TestContext.Current.CancellationToken);

        Assert.Collection(
            servers,
            server => Assert.Equal((3, "alpha"), (server.Id, server.Name)),
            server => Assert.Equal((1, "zeta"), (server.Id, server.Name)));
    }
}
