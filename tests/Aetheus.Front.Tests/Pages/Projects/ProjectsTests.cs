// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using ProjectsPage = Aetheus.Front.Pages.Projects.Projects;
namespace Aetheus.Front.Tests.Pages;

public class ProjectsTests : BunitContext
{
    public ProjectsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_ProjectList()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items =
            [
                new ProjectDto
                {
                    Id = 1,
                    Name = "Web App",
                    Description = "Main web application",
                    Status = ProjectStatus.Active,
                    Tags = ["web", "dotnet"]
                }
            ],
            TotalCount = 1
        });

        var cut = Render<ProjectsPage>();
        cut.WaitForState(() => cut.Markup.Contains("Web App"), TimeSpan.FromSeconds(2));
        Assert.Contains("Web App", cut.Markup);
    }

    [Fact]
    public void Renders_Empty()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [],
            TotalCount = 0
        });

        var cut = Render<ProjectsPage>();
        cut.WaitForState(() => cut.Markup.Contains("NoProjectsFound"), TimeSpan.FromSeconds(2));
        Assert.Contains("NoProjectsFound", cut.Markup);
    }

    [Fact]
    public async Task NewProject_OpensDialog()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });

        var cut = Render<ProjectsPage>();
        var dialog = Services.GetRequiredService<DialogService>();
        var opened = false;
        dialog.OnOpen += (_, _, _, _) => opened = true;

        // The "New project" button now opens the create dialog instead of navigating. Start the
        // (un-awaited) open, assert OnOpen fired, then close and await - the dialog never resolves
        // on its own, so we must close it to complete the captured task (no WaitForState on it).
        var method = typeof(ProjectsPage).GetMethod("OpenCreateDialog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        await cut.InvokeAsync(() => dialog.Close(null));
        await task;

        Assert.True(opened);
    }
}
