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

    public PipelinesListTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/pipelines/favorites", new PipelineFavoritesDto());
        _handler.SetJsonResponse("api/pipelines/fleet", new PaginatedResult<PipelineFleetItemDto>());
    }

    [Fact]
    public void FavoriteState_IsLoadedAndForwardedToPipelineGrid()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
            [new PipelineDependencyDto { Id = 7, Name = "Build" }]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/favorites", new PipelineFavoritesDto { PipelineIds = [7] });

        var cut = Render<PipelinesList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("RemovePipelineFromFavorites", cut.Markup);
            Assert.Single(cut.FindAll(".pipeline-favorite-card"));
            Assert.Contains("Build", cut.Find(".pipeline-favorite-card").TextContent);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TemplateFilter_ShowsOnlyPipelinesUsingSelectedModel()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
        [
            new PipelineDependencyDto { Id = 7, Name = "Build" },
            new PipelineDependencyDto { Id = 8, Name = "Deploy" }
        ]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/fleet", new PaginatedResult<PipelineFleetItemDto>
        {
            TotalCount = 2,
            Items =
            [
                new() { PipelineId = 7, TemplateId = 3, TemplateName = "ci", Freshness = PipelineFleetFreshness.Current },
                new() { PipelineId = 8, TemplateId = 4, TemplateName = "delivery", Freshness = PipelineFleetFreshness.Current }
            ]
        });

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.TemplateId, 3));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Build", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain(">Deploy<", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("href=\"/templates/3\"", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(1, (int)typeof(PipelinesList).GetField("_catalogTabIndex", Priv)!.GetValue(cut.Instance)!);
        });
    }

    [Fact]
    public void AutonomousFilter_ShowsOnlyPipelinesWithoutModel()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
        [
            new PipelineDependencyDto { Id = 7, Name = "Build" },
            new PipelineDependencyDto { Id = 8, Name = "Standalone" }
        ]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/fleet", new PaginatedResult<PipelineFleetItemDto>
        {
            TotalCount = 2,
            Items =
            [
                new() { PipelineId = 7, TemplateId = 3, TemplateName = "ci", Freshness = PipelineFleetFreshness.Current },
                new() { PipelineId = 8, Freshness = PipelineFleetFreshness.OffCatalog }
            ]
        });

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.TemplateId, -1));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(">Build<", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Standalone", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("AutonomousPipeline", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FavoriteButton_ClickPersistsAndRendersFilledStarWithoutNetworkListener()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
            [new PipelineDependencyDto { Id = 7, Name = "Build" }]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse(
            HttpMethod.Put,
            "api/pipelines/7/favorite",
            new PipelineFavoriteDto { PipelineId = 7, IsFavorite = true });
        var cut = Render<PipelinesList>();
        var button = cut.WaitForElement(
            "button[aria-label='AddPipelineToFavorites']",
            TimeSpan.FromSeconds(2));

        button.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(_handler.Requests, request =>
                request.Method == HttpMethod.Put.Method
                && request.Url.Contains("api/pipelines/7/favorite", StringComparison.Ordinal));
            Assert.Contains("RemovePipelineFromFavorites", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ServerScope_UsesDependencyAndRecentTables_AndPreservesServerLinks()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
        [
            new PipelineDependencyDto
            {
                Id = 7,
                Name = "Server pipeline",
                TriggerType = PipelineTriggerType.Manual,
                RecentRuns = [new PipelineRunSummaryDto { Id = 71, Status = PipelineStatus.Success }]
            }
        ]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>
        {
            new() { Id = 71, PipelineId = 7, PipelineName = "Server pipeline", Status = PipelineStatus.Success }
        });

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        cut.WaitForState(() => cut.Markup.Contains("Server pipeline"), TimeSpan.FromSeconds(2));

        Assert.Contains(_handler.Requests, request => request.Url.Contains("dependencies?serverId=42"));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("runs/recent?serverId=42"));
        Assert.Contains("href=\"/pipelines/7?serverId=42\"", cut.Markup);
        Assert.Contains("href=\"/pipelines/runs/71?serverId=42\"", cut.Markup);
        Assert.Single(cut.FindComponents<PipelineDependencyGrid>());
        Assert.Equal(42, cut.FindComponent<PipelineRunsGrid>().Instance.ServerId);
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
        Assert.Contains(">14<", cut.Markup);
        Assert.Contains("SearchPipelines", cut.Markup);
        Assert.Contains("FavoritesOnly", cut.Markup);
        Assert.DoesNotContain(">Type<", cut.Markup);
        Assert.DoesNotContain("PipelineReferences", cut.Markup);
    }

    [Fact]
    public void ProjectScope_RendersUnifiedWorkspaceAndFiltersImmediately()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
        [
            new PipelineDependencyDto { Id = 8, Name = "Build", ProjectId = 3, TriggerType = PipelineTriggerType.Manual },
            new PipelineDependencyDto { Id = 9, Name = "Deploy", ProjectId = 3, TriggerType = PipelineTriggerType.Webhook }
        ]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/favorites", new PipelineFavoritesDto { PipelineIds = [8] });

        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ProjectId, 3));
        cut.WaitForState(() => cut.FindComponents<PipelineDependencyGrid>().Count == 1, TimeSpan.FromSeconds(2));

        var workspace = cut.Find(".pipeline-workspace");
        Assert.NotNull(workspace.QuerySelector(".pipeline-favorites-section .pipeline-favorite-card"));
        Assert.NotNull(workspace.QuerySelector(".pipeline-catalog-section .pipeline-catalog-header"));
        Assert.Null(workspace.QuerySelector(".pipeline-overview-section-recent"));
        Assert.Single(cut.FindAll(".pipeline-recent-title"));

        cut.Find(".pipeline-list-search").Input("Build");

        cut.WaitForAssertion(() =>
        {
            var grid = cut.FindComponent<PipelineDependencyGrid>().Instance;
            Assert.Single(grid.Items);
            Assert.Equal("Build", grid.Items[0].Name);
        });
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
        Assert.Contains(">12<", cut.Markup);
        Assert.Single(cut.FindComponents<PipelineDependencyGrid>());
        Assert.True(cut.FindComponent<PipelineDependencyGrid>().Instance.ShowsChildren);
        Assert.True(cut.FindComponent<PipelineDependencyGrid>().Instance.Virtualize);
        cut.FindAll(".rz-tabview-nav button")[1].Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindComponents<PipelineDependencyGrid>());
            Assert.False(cut.FindComponent<PipelineDependencyGrid>().Instance.ShowsChildren);
        });
        var runsGrid = cut.FindComponent<PipelineRunsGrid>().Instance;
        Assert.True(runsGrid.Virtualize);
        Assert.True(runsGrid.FillHeight);
        Assert.Contains("pipeline-grid-fill", cut.Markup);
        Assert.DoesNotContain("aria-label=\"Pagination\"", cut.Markup);
        Assert.Contains("Edit", cut.Markup);
        Assert.Contains("Run", cut.Markup);
    }

    [Fact]
    public void ServerScope_RendersTheSameOverviewStructureAsProjectScope()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        cut.WaitForState(() => _handler.Requests.Any(request => request.Url.Contains("runs/recent")), TimeSpan.FromSeconds(2));

        Assert.Contains("PipelinesWithoutChildren", cut.Markup);
        Assert.Contains("RecentRuns", cut.Markup);
        Assert.DoesNotContain("progressbar", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HubEvent_ServerScope_RefreshesForNewPipelineId()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups(leaves:
            [new PipelineDependencyDto { Id = 11, Name = "Current", TriggerType = PipelineTriggerType.Manual }]));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ServerId, 42));
        cut.WaitForState(() => cut.Markup.Contains("Current"), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies"));
        var method = typeof(PipelinesList).GetMethod("OnPipelineHubEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [999])!);
        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies")) > before),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task HubEvent_ProjectScope_RefreshesForNewPipelineId()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>(parameters => parameters.Add(component => component.ProjectId, 7));
        cut.WaitForState(() => _handler.Requests.Any(request => request.Url.Contains("api/pipelines/dependencies")), TimeSpan.FromSeconds(2));
        var before = _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies"));
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>
        {
            new() { Id = 123, PipelineId = 999, PipelineName = "Live pipeline", Status = PipelineStatus.Running }
        });
        var method = typeof(PipelinesList).GetMethod("OnPipelineHubEvent", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [999])!);

        cut.WaitForAssertion(() => Assert.True(
            _handler.Requests.Count(request => request.Url.Contains("api/pipelines/dependencies")) > before),
            TimeSpan.FromSeconds(3));
        cut.WaitForAssertion(() => Assert.Contains(">123<", cut.Markup), TimeSpan.FromSeconds(3));
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
        cut.WaitForState(() => cut.Markup.Contains(">20<"), TimeSpan.FromSeconds(2));

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
        typeof(PipelinesList).GetField("_favoritesOnly", Priv)!.SetValue(cut.Instance, true);

        await cut.InvokeAsync(() => (Task)typeof(PipelinesList).GetMethod("ClearFilters", Priv)!.Invoke(cut.Instance, [])!);

        Assert.Null(typeof(PipelinesList).GetField("_search", Priv)!.GetValue(cut.Instance));
        Assert.Null(typeof(PipelinesList).GetField("_triggerFilter", Priv)!.GetValue(cut.Instance));
        Assert.False((bool)typeof(PipelinesList).GetField("_favoritesOnly", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task NewPipeline_NavigatesToCreatePage()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", Groups());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        var cut = Render<PipelinesList>();

        await cut.InvokeAsync(() => typeof(PipelinesList).GetMethod("NewPipeline", Priv)!.Invoke(cut.Instance, []));

        Assert.Contains("pipelines/setup", Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().Uri);
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
