// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Covers the initial-connect retry loop (findings #2/#3: a backend down at first load, or a failed
/// manual reconnect, must still arm a retry - SignalR's automatic-reconnect only engages after a FIRST
/// successful connect) and the reconnect-preserves-tasks fix (finding #4: ReconnectAsync must not blank
/// the visible task list for the duration of the attempt) added to TaskTrackerService.
/// </summary>
public class TaskTrackerServiceReconnectTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public TaskTrackerServiceReconnectTests() => BunitTestHelper.RegisterServices(this);

    private TaskTrackerService CreateService() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger<TaskTrackerService>.Instance);

    private static ServerTaskDto MakeTask(int id, TaskExecutionStatus status = TaskExecutionStatus.Running) =>
        new() { Id = id, Name = $"t-{id}", Status = status, CreatedAt = DateTime.UtcNow.AddSeconds(-id) };

    private static void CallUpsert(TaskTrackerService sut, ServerTaskDto task) =>
        typeof(TaskTrackerService).GetMethod("Upsert", Priv)!.Invoke(sut, [task]);

    private static object? GetPrivate(TaskTrackerService sut, string field) =>
        typeof(TaskTrackerService).GetField(field, Priv)!.GetValue(sut);

    // ── finding #2/#3: a failed initial connect arms a retry loop ─────────────

    [Fact]
    public async Task StartAsync_InitialConnectFails_SchedulesRetryLoop()
    {
        // The harness's HubConnectionFactory always fails to connect (ImmediateFailHandler), so this
        // exercises the real failure path exactly as a backend-down-at-login would.
        var sut = CreateService();
        var attempts = new List<int>();
        sut.OnReconnectAttempt += d => attempts.Add(d);

        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);

        // The hub stays assigned (matches the established "swallow and keep the hub" contract the
        // other TaskTrackerService test files already rely on) - but a retry loop must now be armed
        // instead of leaving the connection dead forever.
        Assert.NotNull(GetPrivate(sut, "_hub"));
        Assert.NotNull(GetPrivate(sut, "_startRetryCts"));
        // The loop announces its first (zero-delay) attempt synchronously before StartAsync returns.
        Assert.Contains(0, attempts);

        await sut.StopAsync(); // stop the background loop before the test ends
    }

    [Fact]
    public async Task StartAsync_SecondCallWhileRetrying_StaysIdempotent()
    {
        var sut = CreateService();
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);
        var hubBefore = GetPrivate(sut, "_hub");

        // A second StartAsync call while the retry loop is still armed must remain a no-op (the
        // existing "_hub is not null -> return" guard), not spawn a competing retry loop.
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Same(hubBefore, GetPrivate(sut, "_hub"));

        await sut.StopAsync();
    }

    // ── finding #4: ReconnectAsync must not blank the visible list ────────────

    [Fact]
    public async Task ReconnectAsync_PreservesVisibleTasks_DuringTheAttempt()
    {
        var sut = CreateService();
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken); // creates a (never-connected) hub, arms the retry loop
        CallUpsert(sut, MakeTask(1));
        CallUpsert(sut, MakeTask(2));
        Assert.Equal(2, sut.Count);

        // ReconnectAsync tears down the old hub and starts a fresh (also failing) one. The visible
        // list must NOT be wiped to empty just because the attempt is in flight / fails again.
        await sut.ReconnectAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(2, sut.Count);

        await sut.StopAsync();
    }

    [Fact]
    public async Task StopAsync_ClearTasksFalse_TearsDownHubButKeepsTasks()
    {
        var sut = CreateService();
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);
        CallUpsert(sut, MakeTask(10));
        Assert.Equal(1, sut.Count);

        await sut.StopAsync(clearTasks: false);

        Assert.Null(GetPrivate(sut, "_hub")); // hub genuinely torn down
        Assert.Equal(1, sut.Count);           // but the visible list survives
    }

    [Fact]
    public async Task StopAsync_Default_StillClearsTasks()
    {
        // Regression guard: the new overload's default must match the pre-existing logout/dispose
        // contract (clear on stop) - only ReconnectAsync opts into clearTasks: false.
        var sut = CreateService();
        await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);
        CallUpsert(sut, MakeTask(20));
        Assert.Equal(1, sut.Count);

        await sut.StopAsync();

        Assert.Equal(0, sut.Count);
    }
}
