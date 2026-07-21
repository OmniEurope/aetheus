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
            StartedAt = startedAt.AddHours(-1)
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
}
