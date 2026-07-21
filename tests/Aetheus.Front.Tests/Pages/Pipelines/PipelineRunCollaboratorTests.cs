// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public class PipelineRunCollaboratorTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public PipelineRunCollaboratorTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public async Task ChildAggregator_OneChildFailure_PreservesSuccessfulChildren()
    {
        _handler.SetResponse("api/pipelines/runs/10", System.Net.HttpStatusCode.ServiceUnavailable);
        _handler.SetJsonResponse("api/pipelines/runs/11", new PipelineRunDto
        {
            Id = 11,
            Status = PipelineStatus.Success,
            TestResultSummary = new PipelineTestResultSummaryDto { TotalTests = 4, Passed = 4 }
        });
        var parent = new PipelineRunDto
        {
            Id = 1,
            Steps =
            [
                new PipelineStepRunDto { Id = 1, TriggeredRunId = 10 },
                new PipelineStepRunDto { Id = 2, TriggeredRunId = 11 }
            ]
        };

        var result = await PipelineRunChildAggregator.LoadAsync(
            parent, Services.GetRequiredService<ApiClient>(), Xunit.TestContext.Current.CancellationToken);

        Assert.Single(result.Children);
        Assert.True(result.Children.ContainsKey(11));
        Assert.Equal(4, result.Run.TestResultSummary?.TotalTests);
    }

    [Fact]
    public async Task ChildAggregator_ManyChildren_BoundsConcurrentRequests()
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var requestCount = 0;
        var concurrencyGate = new object();
        _handler.SetAsyncJsonResponse(
            HttpMethod.Get,
            "api/pipelines/runs/",
            async ct =>
            {
                Interlocked.Increment(ref requestCount);
                var current = Interlocked.Increment(ref inFlight);
                lock (concurrencyGate) maxInFlight = Math.Max(maxInFlight, current);
                try
                {
                    await Task.Delay(20, ct);
                    return new PipelineRunDto { Id = current };
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            });
        var parent = CreateParentWithChildren(12);

        await PipelineRunChildAggregator.LoadAsync(
            parent, Services.GetRequiredService<ApiClient>(), Xunit.TestContext.Current.CancellationToken);

        Assert.InRange(maxInFlight, 2, 4);
        Assert.Equal(12, requestCount);
    }

    [Fact]
    public async Task ChildAggregator_Cancellation_CancelsPendingRequests()
    {
        var startedRequests = 0;
        _handler.SetAsyncJsonResponse(
            HttpMethod.Get,
            "api/pipelines/runs/",
            async ct =>
            {
                Interlocked.Increment(ref startedRequests);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new PipelineRunDto();
            });
        using var cancellation = new CancellationTokenSource();
        var load = PipelineRunChildAggregator.LoadAsync(
            CreateParentWithChildren(12), Services.GetRequiredService<ApiClient>(), cancellation.Token);
        await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.InRange(startedRequests, 1, 4);
    }

    private static PipelineRunDto CreateParentWithChildren(int count) => new()
    {
        Id = 1,
        Steps = Enumerable.Range(1, count)
            .Select(id => new PipelineStepRunDto { Id = id, TriggeredRunId = id + 100 })
            .ToList()
    };

    [Fact]
    public async Task LiveConnection_DisposeCancelsScheduledReload()
    {
        var reloadCount = 0;
        var connection = new PipelineRunLiveConnection(
            Services.GetRequiredService<HubConnectionFactory>(),
            NullLogger.Instance,
            1,
            () => [],
            () => { reloadCount++; return Task.CompletedTask; },
            callback => callback());
        var schedule = typeof(PipelineRunLiveConnection).GetMethod("ScheduleReloadAsync", Priv)!;

        await (Task)schedule.Invoke(connection, [])!;
        await connection.DisposeAsync();
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(0, reloadCount);
    }

    [Fact]
    public async Task LogStreamer_FallbackIsSingleAndRejectsLateTaskLines()
    {
        _handler.SetJsonResponse("api/logs/task/20", new List<TaskLogDto>());
        var cache = new Dictionary<int, List<TaskLogDto>>();
        var streamer = new PipelineRunLogStreamer(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<HubConnectionFactory>(),
            NullLogger.Instance,
            cache,
            callback => callback(),
            () => { });
        var selected = new PipelineStepRunDto
        {
            Id = 1,
            TaskId = 20,
            Status = TaskExecutionStatus.Running
        };

        await streamer.UpdateAsync(selected);
        var firstPoll = typeof(PipelineRunLogStreamer).GetField("_pollTask", Priv)!.GetValue(streamer);
        await streamer.UpdateAsync(selected);
        var secondPoll = typeof(PipelineRunLogStreamer).GetField("_pollTask", Priv)!.GetValue(streamer);
        Assert.Same(firstPoll, secondPoll);

        var append = typeof(PipelineRunLogStreamer).GetMethod("AppendStreamedLogsAsync", Priv)!;
        await (Task)append.Invoke(streamer,
            [new[] { new TaskLogDto { TaskId = 19, Message = "late" } }])!;
        Assert.Empty(cache);

        await (Task)append.Invoke(streamer,
            [new[] { new TaskLogDto { TaskId = 20, Message = "current" } }])!;
        Assert.Single(cache[20]);
        await streamer.DisposeAsync();
        await (Task)append.Invoke(streamer,
            [new[] { new TaskLogDto { TaskId = 20, Message = "after dispose" } }])!;
        Assert.Single(cache[20]);
    }

    [Fact]
    public void PendingReason_UsesApprovalStateInsteadOfServerAssignment()
    {
        var localizer = new BunitTestHelper.StubLocalizer();
        var step = new PipelineStepRunDto
        {
            StageName = "deploy",
            ServerName = "agent-1",
            Status = TaskExecutionStatus.Pending
        };
        var stages = new List<StageViewModel>
        {
            new() { Name = "deploy", Status = TaskExecutionStatus.Pending, Steps = [step] }
        };

        var ordinary = PipelineRunLogView.GetPendingReason(
            step, stages, new PipelineRunDto { Status = PipelineStatus.Running }, localizer);
        var awaitingApproval = PipelineRunLogView.GetPendingReason(
            step, stages, new PipelineRunDto
            {
                Status = PipelineStatus.Running,
                Approvals = [new PipelineApprovalDto { StageName = "deploy", Status = ApprovalStatus.Pending }]
            }, localizer);

        Assert.Equal("WaitingForExecution", ordinary);
        Assert.Equal("WaitingForApproval", awaitingApproval);
    }
}
