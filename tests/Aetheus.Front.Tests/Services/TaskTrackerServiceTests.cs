// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests.Services;

public class TaskTrackerServiceTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TaskTrackerServiceTests() => _handler = BunitTestHelper.RegisterServices(this);

    private TaskTrackerService CreateService() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger<TaskTrackerService>.Instance);

    private static void Invoke(TaskTrackerService sut, string method, params object?[] args) =>
        typeof(TaskTrackerService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, args);

    private static ServerTaskDto Task(int id, TaskExecutionStatus status) =>
        new() { Id = id, Name = $"task-{id}", Status = status, CreatedAt = new DateTime(2026, 1, id) };

    [Fact]
    public void InitialState_IsEmpty()
    {
        var sut = CreateService();
        Assert.Empty(sut.Tasks);
        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public void Upsert_AddsTask_FiresOnChanged()
    {
        var sut = CreateService();
        var changed = 0;
        sut.OnChanged += () => changed++;

        Invoke(sut, "Upsert", Task(1, TaskExecutionStatus.Pending));

        Assert.Equal(1, sut.Count);
        Assert.Equal(1, sut.Tasks[0].Id);
        Assert.True(changed > 0);
    }

    [Fact]
    public void Upsert_SameId_Replaces()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", Task(1, TaskExecutionStatus.Pending));
        Invoke(sut, "Upsert", Task(1, TaskExecutionStatus.Assigned));

        Assert.Equal(1, sut.Count);
        Assert.Equal(TaskExecutionStatus.Assigned, sut.Tasks[0].Status);
    }

    [Fact]
    public void MarkRunning_FlipsStatusToRunning()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", Task(1, TaskExecutionStatus.Pending));

        Invoke(sut, "MarkRunning", 1, (DateTime?)new DateTime(2026, 2, 1));

        Assert.Equal(TaskExecutionStatus.Running, sut.Tasks[0].Status);
    }

    [Fact]
    public void MarkRunning_UnknownId_NoOp()
    {
        var sut = CreateService();
        Invoke(sut, "MarkRunning", 99, (DateTime?)null);
        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public void HandleTerminal_RemovesTaskAndRaisesCompletion()
    {
        var sut = CreateService();
        TaskCompletedNotification? observed = null;
        sut.OnTaskCompleted += notification => observed = notification;
        Invoke(sut, "Upsert", Task(1, TaskExecutionStatus.Running));

        Invoke(sut, "HandleTerminal", new TaskCompletedNotification
        {
            TaskId = 1,
            Status = TaskExecutionStatus.Success
        });

        Assert.Equal(0, sut.Count);
        Assert.Equal(1, observed?.TaskId);
    }

    [Fact]
    public async Task SeedActiveAsync_SeedsFromApi()
    {
        _handler.SetJsonResponse("api/tasks/active", new List<ServerTaskDto> { Task(5, TaskExecutionStatus.Running) });
        var sut = CreateService();

        var seed = (System.Threading.Tasks.Task)typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, [CancellationToken.None])!;
        await seed;

        Assert.Equal(1, sut.Count);
        Assert.Equal(5, sut.Tasks[0].Id);
    }

    [Fact]
    public async Task StartAsync_Unauthenticated_DoesNotCreateHub()
    {
        var unauth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        var sut = new TaskTrackerService(
            Services.GetRequiredService<ApiClient>(),
            unauth,
            Services.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance);

        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);

        var hubField = typeof(TaskTrackerService).GetField("_hub", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Null(hubField.GetValue(sut));
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_NoThrow()
    {
        var sut = CreateService();
        await sut.StopAsync();
        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotStarted_NoThrow()
    {
        var sut = CreateService();
        await sut.DisposeAsync();
        Assert.Equal(0, sut.Count);
    }
}
