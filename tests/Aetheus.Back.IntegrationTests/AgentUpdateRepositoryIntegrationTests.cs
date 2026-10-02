// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AgentUpdateRepositoryIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentReservation_IsIdempotentAndCreatesOneActiveRequest()
    {
        await ResetAndMigrateAsync();
        await using (var seed = NewContext())
        {
            seed.Servers.Add(CurrentServer());
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var release = CurrentRelease();
        var clock = new MutableTimeProvider();
        async Task<(int RequestId, bool Created)> ReserveAsync(string requestedBy)
        {
            await using var db = NewContext();
            var server = await db.Servers.SingleAsync(TestContext.Current.CancellationToken);
            var repository = new AgentUpdateRepository(db, clock);
            var result = await repository.ReserveAsync(
                server, release, requestedBy, TestContext.Current.CancellationToken);
            return (result.Request.Id, result.Created);
        }

        var results = await Task.WhenAll(ReserveAsync("admin-a"), ReserveAsync("admin-b"));

        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].RequestId, results[1].RequestId);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.AgentUpdateRequests.CountAsync(
            request => request.IsActive,
            TestContext.Current.CancellationToken));
        Assert.True((await verify.Servers.SingleAsync(
            TestContext.Current.CancellationToken)).AgentUpdateReserved);
    }

    [Fact]
    public async Task Drain_LeavesExistingWorkAloneAndClaimsOnlyOneExclusiveUpdate()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = new Aetheus.Back.Data.AppDbContext(BuildOptions(), clock);
        var server = CurrentServer();
        server.AgentSessionId = "session-old";
        server.AgentSessionFencingToken = 1;
        server.AgentSessionLeaseExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        var running = new ServerTask
        {
            Server = server,
            Name = "long build",
            Command = "build",
            Operation = OperationKind.PipelineCreateRelease,
            Status = TaskExecutionStatus.Running
        };
        db.AddRange(server, running);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        var blocked = await repository.TryQueueAsync(
            reserved.Request.Id, TestContext.Current.CancellationToken);
        Assert.Equal(1, blocked.BlockingTaskCount);
        Assert.Null(blocked.CreatedTask);
        Assert.Equal(TaskExecutionStatus.Running, running.Status);

        running.Status = TaskExecutionStatus.Success;
        clock.Advance(TimeSpan.FromMinutes(1));
        var racedAfterReservation = new ServerTask
        {
            ServerId = server.Id,
            Name = "late work",
            Command = "late",
            Operation = OperationKind.None,
            Status = TaskExecutionStatus.Pending
        };
        db.Tasks.Add(racedAfterReservation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var queued = await repository.TryQueueAsync(
            reserved.Request.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(queued.CreatedTask);
        Assert.Equal(OperationKind.AgentSelfUpdate, queued.CreatedTask.Operation);
        var repeated = await repository.TryQueueAsync(
            reserved.Request.Id, TestContext.Current.CancellationToken);
        Assert.Null(repeated.CreatedTask);
        Assert.Equal(1, await db.Tasks.CountAsync(
            task => task.Operation == OperationKind.AgentSelfUpdate,
            TestContext.Current.CancellationToken));

        var taskRepository = new TaskRepository(db, clock);
        var claimed = await taskRepository.ClaimPendingTasksAsync(
            server.Id, 10, server.AgentSessionId, TestContext.Current.CancellationToken);
        Assert.Equal(OperationKind.AgentSelfUpdate, Assert.Single(claimed).Operation);
        Assert.Equal(TaskExecutionStatus.Pending, racedAfterReservation.Status);
    }

    [Fact]
    public async Task Confirmation_RequiresNewSessionAndExactReleaseContract()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        server.AgentSessionId = "session-old";
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        await repository.TryQueueAsync(reserved.Request.Id, TestContext.Current.CancellationToken);
        await repository.MarkProgressAsync(
            server.Id,
            new AgentUpdateProgressDto
            {
                ServerId = server.Id,
                Phase = AgentUpdatePhase.LaunchingUpdater,
                Percent = 90
            },
            TestContext.Current.CancellationToken);

        var sameSession = await repository.ConfirmFromHeartbeatAsync(
            server.Id,
            ConfirmingHeartbeat("session-old"),
            TestContext.Current.CancellationToken);
        Assert.Null(sameSession);

        var confirmed = await repository.ConfirmFromHeartbeatAsync(
            server.Id,
            ConfirmingHeartbeat("session-new"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(confirmed);
        Assert.Equal(AgentUpdateRequestStatus.Confirmed, confirmed.Status);
        Assert.Equal("session-new", confirmed.ConfirmedSessionId);
        Assert.False(server.AgentUpdateReserved);
        Assert.Equal(TaskExecutionStatus.Success, confirmed.Task!.Status);
        Assert.NotNull(confirmed.Task.CompletedAt);
    }

    [Theory]
    [InlineData("0.8.0", AgentProtocol.CurrentVersion, "wrong-version")]
    [InlineData("1.0.0", 0, "unsupported-protocol")]
    public async Task InvalidRestart_FailsExplicitly(
        string version,
        int protocol,
        string expectedFailureCode)
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        server.AgentSessionId = "session-old";
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        await repository.TryQueueAsync(reserved.Request.Id, TestContext.Current.CancellationToken);
        await repository.MarkProgressAsync(
            server.Id,
            new AgentUpdateProgressDto
            {
                ServerId = server.Id,
                Phase = AgentUpdatePhase.LaunchingUpdater,
                Percent = 90
            },
            TestContext.Current.CancellationToken);

        var failed = await repository.ConfirmFromHeartbeatAsync(
            server.Id,
            ConfirmingHeartbeat("session-new") with
            {
                AgentVersion = version,
                AgentProtocolVersion = protocol
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(failed);
        Assert.Equal(AgentUpdateRequestStatus.Failed, failed.Status);
        Assert.Equal(expectedFailureCode, failed.FailureCode);
        Assert.False(failed.IsActive);
        Assert.False(server.AgentUpdateReserved);
        Assert.Equal(TaskExecutionStatus.Failed, failed.Task!.Status);
    }

    [Fact]
    public async Task OldAgentRestartDuringHandoff_WaitsForTargetVersion()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        server.AgentSessionId = "session-old";
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        await repository.TryQueueAsync(reserved.Request.Id, TestContext.Current.CancellationToken);
        await repository.MarkProgressAsync(
            server.Id,
            new AgentUpdateProgressDto
            {
                ServerId = server.Id,
                Phase = AgentUpdatePhase.LaunchingUpdater,
                Percent = 90
            },
            TestContext.Current.CancellationToken);

        var result = await repository.ConfirmFromHeartbeatAsync(
            server.Id,
            ConfirmingHeartbeat("session-intermediate") with
            {
                AgentVersion = server.AgentVersion,
                AgentProtocolVersion = server.AgentProtocolVersion
            },
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.True(reserved.Request.IsActive);
        Assert.Equal(AgentUpdateRequestStatus.Handoff, reserved.Request.Status);
        Assert.True(server.AgentUpdateReserved);
        Assert.Equal(TaskExecutionStatus.Pending, reserved.Request.Task!.Status);
    }

    [Fact]
    public async Task ConfirmationTimeout_FailsAndReleasesReservation()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        await repository.TryQueueAsync(reserved.Request.Id, TestContext.Current.CancellationToken);
        await repository.MarkProgressAsync(
            server.Id,
            new AgentUpdateProgressDto
            {
                ServerId = server.Id,
                Phase = AgentUpdatePhase.LaunchingUpdater,
                Percent = 90
            },
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(6));

        var expired = await repository.FailExpiredAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(expired);
        Assert.Equal("confirmation-timeout", request.FailureCode);
        Assert.Equal(AgentUpdateRequestStatus.Failed, request.Status);
        Assert.False(server.AgentUpdateReserved);
        Assert.Equal(TaskExecutionStatus.Timeout, request.Task!.Status);
    }

    [Fact]
    public async Task Handoff_IgnoresGenericTaskTimeoutUntilNewSessionConfirms()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        server.AgentSessionId = "session-old";
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        await repository.TryQueueAsync(reserved.Request.Id, TestContext.Current.CancellationToken);
        await repository.MarkProgressAsync(
            server.Id,
            new AgentUpdateProgressDto
            {
                ServerId = server.Id,
                Phase = AgentUpdatePhase.LaunchingUpdater,
                Percent = 90
            },
            TestContext.Current.CancellationToken);

        // Reproduce the production race: the generic watchdog observes the expected process
        // replacement and settles the old session's task before the confirming heartbeat arrives.
        reserved.Request.Task!.Status = TaskExecutionStatus.Timeout;
        reserved.Request.Task.CompletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prematurelyExpired = await repository.FailExpiredAsync(
            TestContext.Current.CancellationToken);

        Assert.Empty(prematurelyExpired);
        Assert.True(reserved.Request.IsActive);
        Assert.Equal(AgentUpdateRequestStatus.Handoff, reserved.Request.Status);
        Assert.True(server.AgentUpdateReserved);

        var confirmed = await repository.ConfirmFromHeartbeatAsync(
            server.Id,
            ConfirmingHeartbeat("session-new"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(confirmed);
        Assert.Equal(AgentUpdateRequestStatus.Confirmed, confirmed.Status);
        Assert.False(confirmed.IsActive);
        Assert.False(server.AgentUpdateReserved);
        Assert.Equal(TaskExecutionStatus.Success, confirmed.Task!.Status);
        Assert.Equal(0, confirmed.Task.ExitCode);
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Timeout)]
    [InlineData(TaskExecutionStatus.Failed)]
    [InlineData(TaskExecutionStatus.Cancelled)]
    public async Task PreHandoffTerminalTask_FailsAndReleasesReservation(
        TaskExecutionStatus terminalStatus)
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        var queued = await repository.TryQueueAsync(
            reserved.Request.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(queued.CreatedTask);
        queued.CreatedTask.Status = terminalStatus;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var expired = await repository.FailExpiredAsync(TestContext.Current.CancellationToken);

        var failed = Assert.Single(expired);
        Assert.Equal("update-task-timeout", failed.FailureCode);
        Assert.False(failed.IsActive);
        Assert.False(server.AgentUpdateReserved);
    }

    [Fact]
    public async Task PreHandoffDeadline_CancelsTaskAndReleasesReservation()
    {
        await ResetAndMigrateAsync();
        var clock = new MutableTimeProvider();
        await using var db = NewContext();
        var server = CurrentServer();
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AgentUpdateRepository(db, clock);
        var reserved = await repository.ReserveAsync(
            server, CurrentRelease(), "admin", TestContext.Current.CancellationToken);
        var queued = await repository.TryQueueAsync(
            reserved.Request.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(queued.CreatedTask);
        clock.Advance(TimeSpan.FromMinutes(11));

        var expired = await repository.FailExpiredAsync(TestContext.Current.CancellationToken);

        var failed = Assert.Single(expired);
        Assert.Equal("update-task-timeout", failed.FailureCode);
        Assert.Equal(TaskExecutionStatus.Cancelled, queued.CreatedTask.Status);
        Assert.NotNull(queued.CreatedTask.CompletedAt);
        Assert.False(server.AgentUpdateReserved);
    }

    private static Server CurrentServer() => new()
    {
        Name = "agent-1",
        Hostname = "agent-1",
        Organization = new Organization
        {
            Name = "Agent update tests",
            Slug = $"agent-update-{Guid.NewGuid():N}",
            Description = "Agent update integration tests"
        },
        Status = ServerStatus.Online,
        AgentVersion = "0.9.0",
        AgentProtocolVersion = AgentProtocol.CurrentVersion,
        AgentCapabilitiesJson = "[\"agent.self-update\",\"pipeline.build\",\"shell.execute\"]",
        LastHeartbeat = new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc)
    };

    private static AgentReleaseManifestDto CurrentRelease() => new()
    {
        SoftwareVersion = "1.0.0",
        ProtocolVersion = AgentProtocol.CurrentVersion,
        MinimumSupportedProtocol = AgentProtocol.MinimumSupportedVersion,
        MaximumSupportedProtocol = AgentProtocol.MaximumSupportedVersion,
        SoftwareCapabilities = [AgentCapabilities.SelfUpdate, AgentCapabilities.PipelineBuild]
    };

    private static ServerHeartbeatDto ConfirmingHeartbeat(string sessionId) => new()
    {
        AgentSessionId = sessionId,
        AgentVersion = "1.0.0",
        AgentProtocolVersion = AgentProtocol.CurrentVersion,
        AgentCapabilities =
        [
            AgentCapabilities.SelfUpdate,
            AgentCapabilities.ShellExecution,
            AgentCapabilities.PipelineBuild
        ]
    };

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
