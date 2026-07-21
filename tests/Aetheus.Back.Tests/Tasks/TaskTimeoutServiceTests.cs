// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Tasks;

public class TaskTimeoutServiceTests
{
    private static (TaskTimeoutService Sut, ITaskRepository Repo, IClientProxy Proxy) BuildSut(FakeTimeProvider clock)
    {
        var repo = Substitute.For<ITaskRepository>();
        // Default empty sweeps; individual tests override the state(s) they exercise.
        repo.GetStaleRunningTasksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([]);
        repo.GetStaleAssignedTasksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([]);
        repo.GetStalePendingTasksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([]);

        var sp = new ServiceCollection().AddScoped(_ => repo).BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<TaskTimeoutService>>();
        var options = Options.Create(new BackgroundServicesOptions());

        var proxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(proxy);
        var hub = Substitute.For<IHubContext<ServerHub>>();
        hub.Clients.Returns(clients);

        return (new TaskTimeoutService(scopeFactory, logger, options, hub, clock), repo, proxy);
    }

    [Fact]
    public async Task CheckStaleTasksAsync_MarksStuckRunningTaskTimedOutAndSaves()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var (sut, repo, _) = BuildSut(clock);

        var staleTask = new ServerTask { Id = 7, Name = "stuck", Status = TaskExecutionStatus.Running };
        repo.GetStaleRunningTasksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([staleTask]);

        await sut.CheckStaleTasksAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Timeout, staleTask.Status);
        Assert.Equal(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc), staleTask.CompletedAt);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckStaleTasksAsync_MarksStuckPendingTaskTimedOutAndBroadcasts()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var (sut, repo, proxy) = BuildSut(clock);

        // The apache2-install regression: a Pending task no agent ever claimed.
        var pending = new ServerTask { Id = 9, ServerId = 3, Name = "Install - apache2", Status = TaskExecutionStatus.Pending };
        repo.GetStalePendingTasksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([pending]);

        await sut.CheckStaleTasksAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Timeout, pending.Status);
        Assert.Equal(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc), pending.CompletedAt);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        // Per-server group + all-servers group => two TaskCompleted sends.
        await proxy.Received(2).SendCoreAsync("TaskCompleted", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckStaleTasksAsync_UsesShortAssignedStartTimeout()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var (sut, repo, _) = BuildSut(clock);

        await sut.CheckStaleTasksAsync(TestContext.Current.CancellationToken);

        await repo.Received(1).GetStaleAssignedTasksAsync(
            TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>());
        await repo.Received(1).GetStaleRunningTasksAsync(
            TimeSpan.FromMinutes(30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckStaleTasksAsync_NoStaleTasks_DoesNotSaveOrBroadcast()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var (sut, repo, proxy) = BuildSut(clock);

        await sut.CheckStaleTasksAsync(TestContext.Current.CancellationToken);

        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await proxy.DidNotReceive().SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
