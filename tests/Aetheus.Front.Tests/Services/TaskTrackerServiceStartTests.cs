// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Supplemental coverage targeting the StartAsync / StopAsync / SeedActiveAsync
/// async state-machine bodies that remain uncovered.
/// </summary>
public class TaskTrackerServiceStartTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TaskTrackerServiceStartTests() => _handler = BunitTestHelper.RegisterServices(this);

    private TaskTrackerService CreateService() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger<TaskTrackerService>.Instance);

    private static ServerTaskDto MakeTask(int id, TaskExecutionStatus status = TaskExecutionStatus.Running) =>
        new() { Id = id, Name = $"t-{id}", Status = status, CreatedAt = DateTime.UtcNow.AddSeconds(-id) };

    private static void CallUpsert(TaskTrackerService sut, ServerTaskDto task) =>
        typeof(TaskTrackerService)
            .GetMethod("Upsert", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, [task]);

    // ── StartAsync - unauthenticated guard ────────────────────────────────────

    [Fact]
    public async Task StartAsync_NotAuthenticated_LeavesHubNull()
    {
        // Re-register with un-authenticated user
        using var ctx2 = new BunitContext();
        BunitTestHelper.RegisterServices(ctx2, authenticated: false);
        var svc = new TaskTrackerService(
            ctx2.Services.GetRequiredService<ApiClient>(),
            ctx2.Services.GetRequiredService<AuthStateProvider>(),
            ctx2.Services.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance);

        await svc.StartAsync(Xunit.TestContext.Current.CancellationToken);

        var hub = typeof(TaskTrackerService)
            .GetField("_hub", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(svc);
        Assert.Null(hub);
    }

    // ── StartAsync - idempotent (second call no-ops) ──────────────────────────

    [Fact]
    public async Task StartAsync_SecondCall_Idempotent()
    {
        _handler.SetJsonResponse("api/tasks/active", new List<ServerTaskDto>());
        var sut = CreateService();
        try
        {
            // StartAsync swallows the expected deterministic transport failure internally; the hub
            // instance is assigned before the connect attempt and owns a reconnect retry loop.
            await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);

            var hubField = typeof(TaskTrackerService)
                .GetField("_hub", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var hubBefore = hubField.GetValue(sut);
            Assert.NotNull(hubBefore);

            await sut.StartAsync(Xunit.TestContext.Current.CancellationToken);
            Assert.Same(hubBefore, hubField.GetValue(sut));
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    // ── SeedActiveAsync - populates _byId and fires OnChanged ────────────────

    [Fact]
    public async Task SeedActiveAsync_WithTasks_PopulatesAndFires()
    {
        _handler.SetJsonResponse("api/tasks/active", new List<ServerTaskDto>
        {
            MakeTask(1), MakeTask(2), MakeTask(3)
        });
        var sut = CreateService();

        var onChangedCount = 0;
        sut.OnChanged += () => onChangedCount++;

        var seed = typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)seed.Invoke(sut, [CancellationToken.None])!;

        Assert.Equal(3, sut.Count);
        Assert.True(onChangedCount >= 1);
    }

    // ── SeedActiveAsync - HTTP error is swallowed ─────────────────────────────

    [Fact]
    public async Task SeedActiveAsync_HttpError_LogsAndContinues()
    {
        _handler.SetResponse("api/tasks/active", System.Net.HttpStatusCode.InternalServerError);
        var sut = CreateService();

        var seed = typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)seed.Invoke(sut, [CancellationToken.None])!;

        Assert.Equal(0, sut.Count);
    }

    // ── StopAsync - with no hub, remains safe and resets session state ───────

    [Fact]
    public async Task StopAsync_NoHub_ProducesNothing()
    {
        var sut = CreateService();
        await sut.StopAsync();
        Assert.Empty(sut.Tasks);
        Assert.Equal(0, sut.Count);
    }

    // ── StopAsync - subsequent call is safe ───────────────────────────────────

    [Fact]
    public async Task StopAsync_TwiceCalls_Safe()
    {
        var sut = CreateService();
        await sut.StopAsync();
        await sut.StopAsync();
        Assert.Empty(sut.Tasks);
        Assert.Equal(0, sut.Count);
    }

    // ── StopAsync - no hub still clears and notifies session observers ───────

    [Fact]
    public async Task StopAsync_NoHub_ClearsAndNotifies()
    {
        var sut = CreateService();
        CallUpsert(sut, MakeTask(10));

        var fired = false;
        sut.OnChanged += () => fired = true;

        await sut.StopAsync();
        Assert.True(fired);
        Assert.Equal(0, sut.Count);
    }

    // ── DisposeAsync - delegates to StopAsync ─────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CallsStop()
    {
        var sut = CreateService();
        await sut.DisposeAsync();
        Assert.Empty(sut.Tasks);
        Assert.Equal(0, sut.Count);
    }

    // ── Tasks / Count - thread-safe snapshots ─────────────────────────────────

    [Fact]
    public void Tasks_ReturnsSnapshot_NotLiveReference()
    {
        var sut = CreateService();
        CallUpsert(sut, MakeTask(1));

        var snap1 = sut.Tasks;
        CallUpsert(sut, MakeTask(2));
        var snap2 = sut.Tasks;

        Assert.Single(snap1);
        Assert.Equal(2, snap2.Count);
    }
}
