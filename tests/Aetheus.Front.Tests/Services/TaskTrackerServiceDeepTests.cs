// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

public class TaskTrackerServiceDeepTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TaskTrackerServiceDeepTests() => _handler = BunitTestHelper.RegisterServices(this);

    private TaskTrackerService CreateService() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger<TaskTrackerService>.Instance);

    private static void Invoke(TaskTrackerService sut, string method, params object?[] args) =>
        typeof(TaskTrackerService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, args);

    private static ServerTaskDto MakeTask(int id, TaskExecutionStatus status = TaskExecutionStatus.Pending) =>
        new() { Id = id, Name = $"task-{id}", Status = status, CreatedAt = new DateTime(2026, 1, 1).AddDays(id) };

    // === AddOrUpdate (Upsert) ===

    [Fact]
    public void Upsert_MultipleItems_OrderedDescByCreatedAt()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(1, TaskExecutionStatus.Pending));
        Invoke(sut, "Upsert", MakeTask(3, TaskExecutionStatus.Running));
        Invoke(sut, "Upsert", MakeTask(2, TaskExecutionStatus.Assigned));

        var tasks = sut.Tasks;
        Assert.Equal(3, tasks.Count);
        // Newest (id=3 → day 4) should be first
        Assert.Equal(3, tasks[0].Id);
    }

    [Fact]
    public void Upsert_FiresOnChanged_EachTime()
    {
        var sut = CreateService();
        var count = 0;
        sut.OnChanged += () => count++;

        Invoke(sut, "Upsert", MakeTask(10));
        Invoke(sut, "Upsert", MakeTask(11));

        Assert.Equal(2, count);
    }

    // === MarkRunning ===

    [Fact]
    public void MarkRunning_UpdatesStartedAt_WhenProvided()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(5, TaskExecutionStatus.Pending));
        var started = new DateTime(2026, 3, 15);
        Invoke(sut, "MarkRunning", 5, (DateTime?)started);

        Assert.Equal(started, sut.Tasks[0].StartedAt);
    }

    [Fact]
    public void MarkRunning_UsesExistingStartedAt_WhenNullProvided()
    {
        var originalStarted = new DateTime(2026, 2, 1);
        var sut = CreateService();
        // Seed via Upsert with a known StartedAt
        var byIdField = typeof(TaskTrackerService).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var byId = (Dictionary<int, ServerTaskDto>)byIdField.GetValue(sut)!;
        byId[7] = MakeTask(7, TaskExecutionStatus.Pending) with { StartedAt = originalStarted };

        Invoke(sut, "MarkRunning", 7, (DateTime?)null);

        Assert.Equal(originalStarted, sut.Tasks[0].StartedAt);
    }

    // === RemoveOnTerminal ===

    [Fact]
    public void RemoveOnTerminal_NonExistentId_NoOnChanged()
    {
        var sut = CreateService();
        var changed = 0;
        sut.OnChanged += () => changed++;

        Invoke(sut, "RemoveOnTerminal", 999);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void RemoveOnTerminal_ExistingId_FiresOnChanged()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(20));
        var changed = 0;
        sut.OnChanged += () => changed++;

        Invoke(sut, "RemoveOnTerminal", 20);

        Assert.Equal(1, changed);
        Assert.Equal(0, sut.Count);
    }

    // === SeedActiveAsync ===

    [Fact]
    public async Task SeedActiveAsync_ClearsExistingThenRefills()
    {
        _handler.SetJsonResponse("api/tasks/active", new List<ServerTaskDto>
        {
            MakeTask(50, TaskExecutionStatus.Running),
            MakeTask(51, TaskExecutionStatus.Pending)
        });
        var sut = CreateService();
        // Pre-seed with unrelated task
        Invoke(sut, "Upsert", MakeTask(99));

        var seedMethod = typeof(TaskTrackerService)
            .GetMethod("SeedActiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)seedMethod.Invoke(sut, [CancellationToken.None])!;

        Assert.Equal(2, sut.Count);
        Assert.DoesNotContain(sut.Tasks, t => t.Id == 99);
    }

    // === StopAsync ===

    [Fact]
    public async Task StopAsync_WithHub_ClearsStateAndDisposesHub()
    {
        var sut = CreateService();
        Invoke(sut, "Upsert", MakeTask(30));
        Assert.Equal(1, sut.Count);

        // Give the service a (never-started) hub so StopAsync runs its real teardown path rather than
        // the no-hub early return (that no-op case is covered by TaskTrackerServiceCoverageTests).
        var hubField = typeof(TaskTrackerService).GetField("_hub", BindingFlags.NonPublic | BindingFlags.Instance)!;
        hubField.SetValue(sut, new HubConnectionBuilder()
            .WithUrl("http://localhost/tasks").Build());

        await sut.StopAsync();

        // StopAsync genuinely clears the tracked tasks AND releases the hub.
        Assert.Equal(0, sut.Count);
        Assert.Null(hubField.GetValue(sut));
    }

    // === Count ===

    [Fact]
    public void Count_ReflectsCurrentItems()
    {
        var sut = CreateService();
        Assert.Equal(0, sut.Count);
        Invoke(sut, "Upsert", MakeTask(1));
        Invoke(sut, "Upsert", MakeTask(2));
        Assert.Equal(2, sut.Count);
        Invoke(sut, "RemoveOnTerminal", 1);
        Assert.Equal(1, sut.Count);
    }
}
