// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectTasksSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectTasksSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyTasks()
    {
        _handler.SetJsonResponse("api/projects/1/tasks", new PaginatedResult<ServerTaskDto> { Items = [], TotalCount = 0 });
        var cut = Render<ProjectTasksSection>(p => p.Add(x => x.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("Tasks"), TimeSpan.FromSeconds(2));
        Assert.Contains("Tasks", cut.Markup);
    }

    [Fact]
    public void Renders_WithTasks()
    {
        _handler.SetJsonResponse("api/projects/1/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items =
            [
                new ServerTaskDto { Id = 1, Name = "deploy", Status = TaskExecutionStatus.Success }
            ],
            TotalCount = 1
        });
        var cut = Render<ProjectTasksSection>(p => p.Add(x => x.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("deploy"), TimeSpan.FromSeconds(2));
        Assert.Contains("deploy", cut.Markup);
    }
}
