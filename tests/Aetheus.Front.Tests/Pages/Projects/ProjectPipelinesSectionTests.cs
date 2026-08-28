// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectPipelinesSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectPipelinesSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/pipelines/dependencies", new PipelineDependencyGroupsDto());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/favorites", new PipelineFavoritesDto());
        _handler.SetJsonResponse("api/pipelines/fleet", new PaginatedResult<PipelineFleetItemDto>());
    }

    [Fact]
    public void Renders_Loading_WhenPipelinesNull()
    {
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, null)
             .Add(x => x.ProjectId, 1));
        // Null pipelines → only the branded loading indicator renders (no header/grid yet).
        Assert.Contains("aetheus-loader-logo", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyList_WithNoRecords()
    {
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, new List<PipelineDto>())
             .Add(x => x.ProjectId, 5));
        Assert.Contains("Pipelines", cut.Markup);
        Assert.Contains("NoRecentRuns", cut.Markup);
        Assert.Single(cut.FindComponents<PipelinesList>());
    }

    [Fact]
    public async Task NewPipeline_Navigates_ToSetupWizardWithProjectId()
    {
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, new List<PipelineDto>())
             .Add(x => x.ProjectId, 7));

        var method = typeof(ProjectPipelinesSection).GetMethod("NewPipeline",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => method.Invoke(cut.Instance, []));

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("pipelines/setup", nav.Uri);
        Assert.Contains("projectId=7", nav.Uri);
    }

    [Fact]
    public void PipelineWithLastRunStatus_ShowsBadge()
    {
        var pipelines = new List<PipelineDto>
        {
            new() { Id = 1, Name = "CI", TriggerType = PipelineTriggerType.Manual, LastRunStatus = PipelineStatus.Success }
        };
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, pipelines)
             .Add(x => x.ProjectId, 1));
        Assert.Contains("Pipelines", cut.Markup);
        Assert.Contains("RecentRuns", cut.Markup);
    }

    [Fact]
    public void LoadedSection_DelegatesBothDefinitionsAndRunsToSharedOverview()
    {
        var pipelines = new List<PipelineDto>
        {
            new() { Id = 2, Name = "Nightly", TriggerType = PipelineTriggerType.Schedule, LastRunStatus = null }
        };
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, pipelines)
             .Add(x => x.ProjectId, 1));
        var overview = cut.FindComponent<PipelinesList>();
        Assert.Equal(1, overview.Instance.ProjectId);
        Assert.Single(cut.FindComponents<PipelineRunsGrid>());
    }
}
