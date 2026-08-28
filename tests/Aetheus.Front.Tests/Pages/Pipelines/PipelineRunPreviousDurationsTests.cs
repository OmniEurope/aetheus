// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public class PipelineRunPreviousDurationsTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PipelineRunPreviousDurationsTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public async Task LoadAsync_ResolvesPreviousRunOnlyForRunningRealTask()
    {
        var startedAt = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Local);
        var current = new PipelineRunDto
        {
            Id = 20,
            PipelineId = 6,
            Status = PipelineStatus.Running,
            StartedAt = startedAt,
            Steps = [new PipelineStepRunDto { StepName = "test", StageName = "build", Status = TaskExecutionStatus.Running }]
        };
        var previous = new PipelineRunDto
        {
            Id = 19,
            PipelineId = 6,
            Status = PipelineStatus.Success,
            StartedAt = startedAt.AddHours(-1),
            Steps =
            [
                new PipelineStepRunDto
                {
                    StepName = "test",
                    StageName = "build",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = startedAt.AddHours(-1),
                    CompletedAt = startedAt.AddHours(-1).AddMinutes(3)
                }
            ]
        };
        _handler.SetJsonResponse("api/pipelines/6/runs?pageSize=50", new PaginatedResult<PipelineRunDto>
        {
            Items = [current, previous],
            TotalCount = 2
        });
        var resolver = new PipelineRunPreviousDurations(Services.GetRequiredService<ApiClient>());

        await resolver.LoadAsync([current]);

        Assert.True(resolver.Runs.TryGetValue(current.Id, out var resolved),
            $"Previous run was not resolved. Requests: {string.Join(", ", _handler.Requests.Select(request => request.Url))}");
        Assert.Equal(previous.Id, resolved.Id);
    }

    [Fact]
    public async Task LoadAsync_DoesNotQueryHistoryForCompletedRun()
    {
        var completed = new PipelineRunDto
        {
            Id = 20,
            PipelineId = 6,
            Status = PipelineStatus.Success,
            Steps = [new PipelineStepRunDto { Status = TaskExecutionStatus.Success }]
        };
        var resolver = new PipelineRunPreviousDurations(Services.GetRequiredService<ApiClient>());

        await resolver.LoadAsync([completed]);

        Assert.Empty(_handler.Requests);
        Assert.Empty(resolver.Runs);
    }

    [Fact]
    public async Task LoadAsync_UsesOlderRunWhenImmediatePreviousRunDidNotReachTheTask()
    {
        var startedAt = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Local);
        var current = new PipelineRunDto
        {
            Id = 30,
            PipelineId = 6,
            Status = PipelineStatus.Running,
            StartedAt = startedAt,
            Steps =
            [
                new PipelineStepRunDto
                {
                    StepName = "ValidatePreviousE2E",
                    StageName = "compatibility",
                    MatrixLeg = null,
                    Status = TaskExecutionStatus.Running
                }
            ]
        };
        var cancelledBeforeTask = new PipelineRunDto
        {
            Id = 29,
            PipelineId = 6,
            Status = PipelineStatus.Cancelled,
            StartedAt = startedAt.AddHours(-1),
            Steps = [new PipelineStepRunDto { StepName = "Checkout", StageName = "prepare" }]
        };
        var comparable = new PipelineRunDto
        {
            Id = 28,
            PipelineId = 6,
            Status = PipelineStatus.Success,
            StartedAt = startedAt.AddHours(-2),
            Steps =
            [
                new PipelineStepRunDto
                {
                    StepName = "validatepreviouse2e",
                    StageName = "Compatibility",
                    MatrixLeg = "",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = startedAt.AddHours(-2),
                    CompletedAt = startedAt.AddHours(-2).AddMinutes(12)
                }
            ]
        };
        _handler.SetJsonResponse("api/pipelines/6/runs?pageSize=50", new PaginatedResult<PipelineRunDto>
        {
            Items = [current, cancelledBeforeTask, comparable],
            TotalCount = 3
        });
        var resolver = new PipelineRunPreviousDurations(Services.GetRequiredService<ApiClient>());

        await resolver.LoadAsync([current]);

        var reference = Assert.Single(resolver.Runs).Value;
        var step = Assert.Single(reference.Steps);
        Assert.Equal(comparable.Steps[0].StartedAt, step.StartedAt);
        Assert.Equal(comparable.Steps[0].CompletedAt, step.CompletedAt);
    }
}
