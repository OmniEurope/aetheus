// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Ai;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Ai;

public sealed class AiPaginationTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AiPaginationTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
        _handler.SetJsonResponse("api/ai/consumption", new AiConsumptionDto());
        _handler.SetJsonResponse("api/ai/tasks", new PaginatedResult<AiTaskDefinitionDto>
        {
            Items = [],
            TotalCount = 101,
            Page = 1,
            PageSize = 25
        });
        _handler.SetJsonResponse("api/ai/profiles", new PaginatedResult<AiRunnerProfileDto>
        {
            Items = [],
            TotalCount = 101,
            Page = 1,
            PageSize = 25
        });
        _handler.SetJsonResponse("api/ai/results", new PaginatedResult<AiRunResultDto>
        {
            Items =
            [
                new AiRunResultDto
                {
                    Id = 101,
                    ProfileName = "reviewer",
                    ReportMarkdown = "page five",
                    CreatedAt = DateTime.UtcNow
                }
            ],
            TotalCount = 101,
            Page = 5,
            PageSize = 25
        });
    }

    [Fact]
    public async Task TasksGrid_PageBeyondHundred_RequestsBackendPage()
    {
        var cut = Render<AiTasks>();

        await cut.InvokeAsync(() => cut.Instance.LoadDataAsync(
            new LoadDataArgs { Skip = 100, Top = 25 }));

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/ai/tasks", StringComparison.Ordinal)
            && request.Url.Contains("page=5", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=25", StringComparison.Ordinal));
    }

    [Fact]
    public void TaskCompletion_FilterRejectsOrdinaryTasksAndAcceptsAiRuns()
    {
        Assert.False(AiTasks.ShouldReloadFor(new TaskCompletedNotification
        {
            Operation = OperationKind.None
        }));
        Assert.True(AiTasks.ShouldReloadFor(new TaskCompletedNotification
        {
            Operation = OperationKind.AiRun
        }));
    }

    [Fact]
    public void TasksGrid_RendersLinkedProjectAndServerSources()
    {
        _handler.SetJsonResponse("api/ai/tasks", new PaginatedResult<AiTaskDefinitionDto>
        {
            Items =
            [
                new AiTaskDefinitionDto { Id = 1, Name = "Project review", ProjectId = 4, ProjectName = "Website" },
                new AiTaskDefinitionDto { Id = 2, Name = "Server review", ServerId = 8, ServerName = "runner-08" }
            ],
            TotalCount = 2
        });

        var cut = Render<AiTasks>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("a[href='/projects/4/overview']"));
            Assert.NotNull(cut.Find("a[href='/servers/8/overview']"));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ProfilesGrid_PageBeyondHundred_RequestsBackendPage()
    {
        var cut = Render<AiRunnerProfiles>();

        await cut.InvokeAsync(() => cut.Instance.LoadDataAsync(
            new LoadDataArgs { Skip = 100, Top = 25 }));

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/ai/profiles", StringComparison.Ordinal)
            && request.Url.Contains("page=5", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=25", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResultsDialog_PageBeyondHundred_RequestsBackendPage()
    {
        var cut = Render<AiResultDialog>(
            parameters => parameters.Add(component => component.DefinitionId, 7));

        await cut.InvokeAsync(() => cut.Instance.OnPageChangedAsync(
            new PagerEventArgs { Skip = 100 }));

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/ai/results", StringComparison.Ordinal)
            && request.Url.Contains("page=5", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=25", StringComparison.Ordinal)
            && request.Url.Contains("definitionId=7", StringComparison.Ordinal));
    }
}
