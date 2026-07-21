// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Shared;

public class PipelinesListTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public PipelinesListTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void ServerScope_UsesPagedEndpoint_AndPreservesServerLinks()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies/page", Page(
            new PipelineDependencyDto
            {
                Id = 7,
                Name = "Server pipeline",
                TriggerType = PipelineTriggerType.Manual,
                RecentRuns = [new PipelineRunSummaryDto { Id = 71, Status = PipelineStatus.Success }]
            }));

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        cut.WaitForState(() => cut.Markup.Contains("Server pipeline"), TimeSpan.FromSeconds(2));

        Assert.Contains("serverId=42", Assert.Single(_handler.Requests).Url);
        Assert.Contains("href=\"/pipelines/7?serverId=42\"", cut.Markup);
        Assert.Contains("href=\"/pipelines/runs/71?serverId=42\"", cut.Markup);
    }

    [Fact]
    public void ProjectScope_PreservesProjectLinks()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
            [new PipelineDependencyDto { Id = 8, Name = "Project pipeline", ProjectId = 3, TriggerType = PipelineTriggerType.Webhook }]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>
        {
            new() { Id = 14, PipelineId = 8, PipelineName = "Project pipeline", Status = PipelineStatus.Success }
        });

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ProjectId, 3));
        cut.WaitForState(() => cut.Markup.Contains("Project pipeline"), TimeSpan.FromSeconds(2));

        Assert.Contains(_handler.Requests, request => request.Url.Contains("api/pipelines/dependencies"));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("runs/recent?projectId=3"));
        Assert.Contains("href=\"/pipelines/8?projectId=3\"", cut.Markup);
        Assert.Contains("#14", cut.Markup);
        Assert.DoesNotContain(">Type<", cut.Markup);
        Assert.DoesNotContain("PipelineReferences", cut.Markup);
    }

    [Fact]
    public void GlobalScope_RendersParentAndResolvedReference()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(parents:
        [
            new PipelineDependencyDto
            {
                Id = 9,
                Name = "release",
                ProjectName = "Aetheus",
                TriggerType = PipelineTriggerType.Manual,
                References = [new PipelineDependencyReferenceDto(10, "deploy")]
            }
        ], leaves:
        [
            new PipelineDependencyDto
            {
                Id = 10,
                Name = "deploy",
                ProjectName = "Aetheus",
                Parents = [new PipelineDependencyReferenceDto(9, "release")]
            }
        ]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>
        {
            new() { Id = 12, PipelineId = 10, PipelineName = "deploy", ProjectName = "Aetheus", Status = PipelineStatus.Success }
        });

        var cut = Render<PipelinesList>();
        cut.WaitForState(() => cut.Markup.Contains("release"), TimeSpan.FromSeconds(2));

        Assert.Contains("PipelinesWithChildren", cut.Markup);
        Assert.Contains("PipelinesWithoutChildren", cut.Markup);
        Assert.Contains("RecentRuns", cut.Markup);
        Assert.Contains("#12", cut.Markup);
        Assert.Equal(2, cut.FindComponents<PipelineDependencyGrid>().Count);
        Assert.All(cut.FindComponents<PipelineDependencyGrid>(), grid => Assert.True(grid.Instance.Virtualize));
        var runsGrid = cut.FindComponent<PipelineRunsGrid>().Instance;
        Assert.True(runsGrid.Virtualize);
        Assert.True(runsGrid.FillHeight);
        Assert.Contains("pipeline-grid-fill", cut.Markup);
        Assert.DoesNotContain("aria-label=\"Pagination\"", cut.Markup);
        Assert.Contains("Edit", cut.Markup);
        Assert.Contains("Run", cut.Markup);
    }

    [Fact]
    public async Task OnLoadData_ForwardsRequestedPageAndPageSize()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies/page", Page());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        var method = typeof(PipelinesList).GetMethod("OnLoadData", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new LoadDataArgs { Skip = 50, Top = 25 }])!);

        Assert.Contains(_handler.Requests, request => request.Url.Contains("page=3") && request.Url.Contains("pageSize=25"));
    }

    [Fact]
    public async Task HubEvent_RefreshesOnlyPipelineOnCurrentPage()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies/page", Page(
            new PipelineDependencyDto { Id = 11, Name = "Current", TriggerType = PipelineTriggerType.Manual }));
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        cut.WaitForState(() => cut.Markup.Contains("Current"), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("dependencies/page"));
        var method = typeof(PipelinesList).GetMethod("OnPipelineHubEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [999])!);
        Assert.Equal(before, _handler.Requests.Count(request => request.Url.Contains("dependencies/page")));

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [11])!);
        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("dependencies/page")) > before));
    }

    [Fact]
    public async Task HubEvent_ProjectScope_RefreshesForNewPipelineId()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ProjectId, 7));
        cut.WaitForState(() => _handler.Requests.Any(request => request.Url.Contains("api/pipelines/dependencies")), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies"));
        var method = typeof(PipelinesList).GetMethod("OnPipelineHubEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [999])!);

        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies")) > before),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task PipelineProgressEvent_RefreshesGlobalRecentRuns()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>();
        cut.WaitForState(() => _handler.Requests.Any(request => request.Url.Contains("runs/recent")), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("runs/recent"));
        var method = typeof(PipelinesList).GetMethod("OnPipelineProgressEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("runs/recent")) > before),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task PipelineProgressEvent_RefreshesProjectRecentRuns()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ProjectId, 7));
        cut.WaitForState(() => _handler.Requests.Any(request => request.Url.Contains("runs/recent?projectId=7")), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("runs/recent?projectId=7"));
        var method = typeof(PipelinesList).GetMethod("OnPipelineProgressEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("runs/recent?projectId=7")) > before),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void RecentRuns_AreDefensivelyLimitedToTwenty()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", Enumerable.Range(1, 25)
            .Select(id => new PipelineRunDto
            {
                Id = id,
                PipelineId = 1,
                PipelineName = "CI",
                Status = PipelineStatus.Success,
                StartedAt = new DateTime(2026, 1, 1).AddMinutes(id)
            })
            .ToList());

        var cut = Render<PipelinesList>();
        cut.WaitForState(() => cut.Markup.Contains("#20"), TimeSpan.FromSeconds(2));

        Assert.Equal(20, cut.FindComponent<PipelineRunsGrid>().Instance.Items.Count);
    }

    [Fact]
    public async Task ClearFilters_ResetsSearchAndTrigger()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>();
        typeof(PipelinesList).GetField("_search", Priv)!.SetValue(cut.Instance, "build");
        typeof(PipelinesList).GetField("_triggerFilter", Priv)!.SetValue(cut.Instance, PipelineTriggerType.Schedule);

        await cut.InvokeAsync(() => (Task)typeof(PipelinesList).GetMethod("ClearFilters", Priv)!.Invoke(cut.Instance, [])!);

        Assert.Null(typeof(PipelinesList).GetField("_search", Priv)!.GetValue(cut.Instance));
        Assert.Null(typeof(PipelinesList).GetField("_triggerFilter", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task NewPipeline_NavigatesToCreatePage()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>();

        await cut.InvokeAsync(() => typeof(PipelinesList).GetMethod("NewPipeline", Priv)!.Invoke(cut.Instance, []));

        Assert.Contains("pipelines/new", Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().Uri);
    }

    private static PaginatedResult<PipelineDependencyDto> Page(params PipelineDependencyDto[] items) => new()
    {
        Items = items.ToList(),
        TotalCount = items.Length,
        Page = 1,
        PageSize = 25
    };

    private static PipelineDependencyGroupsDto Groups(
        List<PipelineDependencyDto>? parents = null,
        List<PipelineDependencyDto>? leaves = null) => new()
        {
            Parents = parents ?? [],
            Leaves = leaves ?? []
        };
}
