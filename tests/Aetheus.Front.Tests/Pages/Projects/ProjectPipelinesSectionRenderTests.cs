// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectPipelinesSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectPipelinesSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/pipelines/dependencies", new PipelineDependencyGroupsDto());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
    }

    [Fact]
    public void Renders_EmptyPipelines()
    {
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, new List<PipelineDto>()));
        // Loaded (empty) → section chrome renders with no recent runs.
        Assert.Contains("Pipelines", cut.Markup);
        Assert.Contains("NoRecentRuns", cut.Markup);
    }

    [Fact]
    public void Renders_WithPipelines()
    {
        var cut = Render<ProjectPipelinesSection>(p =>
            p.Add(x => x.Pipelines, new List<PipelineDto>
            {
                new() { Id = 1, Name = "CI", LastRunStatus = PipelineStatus.Success },
                new() { Id = 2, Name = "CD", LastRunStatus = PipelineStatus.Running }
            }));
        // The section renders its chrome (Pipelines header + Recent runs heading) around the shared list.
        Assert.Contains("Pipelines", cut.Markup);
        Assert.Contains("RecentRuns", cut.Markup);
    }
}
