// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Additional coverage for TaskTrackerService - StopAsync (with and without hub),
/// SeedActiveAsync failure path, StartAsync when not authenticated, and
/// the TaskStartedEvent private record.
/// </summary>
public class TaskTrackerServiceCoverageTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TaskTrackerServiceCoverageTests() => _handler = BunitTestHelper.RegisterServices(this);

    private TaskTrackerService CreateService() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger<TaskTrackerService>.Instance);

    private static ServerTaskDto MakeTask(int id, TaskExecutionStatus status = TaskExecutionStatus.Pending) =>
        new() { Id = id, Name = $"task-{id}", Status = status, CreatedAt = new DateTime(2026, 1, 1).AddDays(id) };

    private static void Invoke(TaskTrackerService sut, string method, params object?[] args) =>
        typeof(TaskTrackerService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, args);

    // ── StopAsync: session state is cleared even when no hub was established ──

    [Fact]
    public async Task StopAsync_NoHub_ClearsTasksAndNotifies()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(1));
        Assert.Equal(1, sut.Count);

        var fired = 0;
        sut.OnChanged += () => fired++;

        await sut.StopAsync();

        Assert.Equal(0, sut.Count);
        Assert.Equal(1, fired);
    }

    // ── DisposeAsync (with an established hub) clears all tasks and fires OnChanged ──
    // StartAsync (authenticated) creates the hub then fails to *connect* in the test env -
    // the failure is swallowed and `_hub` stays non-null. DisposeAsync → StopAsync then
    // runs the full path: dispose hub, clear _byId, fire OnChanged. We assert that real effect.

    [Fact]
    public async Task DisposeAsync_WithHub_ClearsAllTasks_FiresOnChanged()
    {
        var sut = CreateService();
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken); // creates _hub; connect fails silently, _hub remains non-null

        Invoke(sut, "Upsert", MakeTask(10));
        Invoke(sut, "Upsert", MakeTask(11));
        Assert.Equal(2, sut.Count);

        var changes = 0;
        sut.OnChanged += () => changes++;

        await sut.DisposeAsync();

        Assert.Equal(0, sut.Count);   // tasks really cleared
        Assert.True(changes >= 1);    // OnChanged really fired
    }

    // ── SeedActiveAsync: API returns tasks, replaces existing ────────────────

    [Fact]
    public async Task SeedActiveAsync_PopulatesFromApi()
    {
        _handler.SetJsonResponse("api/tasks/active", new List<ServerTaskDto>
        {
            MakeTask(100, TaskExecutionStatus.Running),
            MakeTask(101, TaskExecutionStatus.Assigned)
        });

        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(999)); // old task

        var seedMethod = typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)seedMethod.Invoke(sut, [CancellationToken.None])!;

        Assert.Equal(2, sut.Count);
        Assert.DoesNotContain(sut.Tasks, t => t.Id == 999);
    }

    // ── SeedActiveAsync: API exception → catch, no throw ─────────────────────

    [Fact]
    public async Task SeedActiveAsync_ApiException_Caught()
    {
        _handler.SetResponse("api/tasks/active", System.Net.HttpStatusCode.InternalServerError);

        var sut = CreateService();
        var seedMethod = typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Should not throw
        await (Task)seedMethod.Invoke(sut, [CancellationToken.None])!;
        Assert.Equal(0, sut.Count);
    }

    // ── StartAsync: not authenticated → no-op ────────────────────────────────

    [Fact]
    public async Task StartAsync_NotAuthenticated_DoesNotStartHub()
    {
        using var ctx = new BunitContext();
        BunitTestHelper.RegisterServices(ctx, authenticated: false);

        var sut = new TaskTrackerService(
            ctx.Services.GetRequiredService<ApiClient>(),
            ctx.Services.GetRequiredService<AuthStateProvider>(),
            ctx.Services.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance);

        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);

        var hub = typeof(TaskTrackerService)
            .GetField("_hub", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sut);
        Assert.Null(hub);
    }

    // ── Upsert, MarkRunning, HandleTerminal interaction ──────────────────────

    [Fact]
    public void FullLifecycle_PendingToRunningToRemoved()
    {
        var sut = CreateService();

        Invoke(sut, "Upsert", MakeTask(50, TaskExecutionStatus.Pending));
        Assert.Equal(1, sut.Count);
        Assert.Equal(TaskExecutionStatus.Pending, sut.Tasks[0].Status);

        Invoke(sut, "MarkRunning", 50, (DateTime?)new DateTime(2026, 6, 1));
        Assert.Equal(TaskExecutionStatus.Running, sut.Tasks[0].Status);

        Invoke(sut, "HandleTerminal", new TaskCompletedNotification
        {
            TaskId = 50,
            Status = TaskExecutionStatus.Success
        });
        Assert.Equal(0, sut.Count);
    }

    // ── HandleTerminal: id not in dict → no OnChanged ────────────────────────

    [Fact]
    public void HandleTerminal_MissingId_DoesNotFireOnChanged()
    {
        var sut = CreateService();
        var fired = 0;
        sut.OnChanged += () => fired++;

        Invoke(sut, "HandleTerminal", new TaskCompletedNotification
        {
            TaskId = 999,
            Status = TaskExecutionStatus.Success
        });

        Assert.Equal(0, fired);
    }

    // ── MarkRunning: id not in dict → does nothing ───────────────────────────

    [Fact]
    public void MarkRunning_MissingId_LeavesTheValueUnchanged()
    {
        var sut = CreateService();
        Invoke(sut, "MarkRunning", 12345, (DateTime?)null);
        Assert.Equal(0, sut.Count);
    }
}
