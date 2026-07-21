// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerProjectsSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerProjectsSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyProjects_ShowsHeadingAndEmptyText()
    {
        _handler.SetJsonResponse("api/servers/1/projects", new PaginatedResult<ProjectDto>());
        var cut = Render<ServerProjectsSection>(p => p.Add(x => x.ServerId, 1));
        cut.WaitForAssertion(() => Assert.Contains("Projects", cut.Markup), TimeSpan.FromSeconds(2));
        Assert.Contains("NoRecords", cut.Markup);
    }

    [Fact]
    public void Renders_WithProjects_ShowsProjectNameAndLink()
    {
        _handler.SetJsonResponse("api/servers/1/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new() { Id = 7, Name = "MyProject", Status = ProjectStatus.Active }],
            TotalCount = 1
        });
        var cut = Render<ServerProjectsSection>(p => p.Add(x => x.ServerId, 1));
        cut.WaitForAssertion(() => Assert.Contains("MyProject", cut.Markup), TimeSpan.FromSeconds(2));
        // The project name renders as a link to the project overview, and the Active status badge shows.
        Assert.Contains("/projects/7/overview", cut.Markup);
        Assert.Contains("Active", cut.Markup);
        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("page=1", StringComparison.Ordinal) &&
            request.Url.Contains("pageSize=25", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerIdChange_ReloadsProjectsOnSameInstance()
    {
        _handler.SetJsonResponse("api/servers/1/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "first-project" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/servers/2/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 2, Name = "second-project" }],
            TotalCount = 1
        });
        var cut = Render<ServerProjectsSection>(p => p.Add(x => x.ServerId, 1));

        cut.Render(p => p.Add(x => x.ServerId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second-project", cut.Markup));
        Assert.DoesNotContain("first-project", cut.Markup);
    }
}
