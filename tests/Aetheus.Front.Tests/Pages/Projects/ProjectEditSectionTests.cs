// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class ProjectEditSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectEditSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=",
            []);
    }

    [Fact]
    public void Renders_FormFields_WhenProjectProvided()
    {
        var project = new ProjectDetailDto
        {
            Id = 1,
            Name = "TestProject",
            Description = "A test project",
            RepositoryUrl = "https://github.com/test",
            DefaultBranch = "main",
            Status = ProjectStatus.Active,
            Tags = ["web", "api"]
        };

        _handler.SetJsonResponse("api/projects/1", project);

        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, project));

        Assert.Contains("TestProject", cut.Markup);
    }

    [Fact]
    public void Renders_LoadingSpinner_WhenNoProject()
    {
        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, null));

        // With Project null the section shows the loading placeholder, not the edit form.
        Assert.Contains("rz-progressbar-circular", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Save"));
    }

    [Fact]
    public void Renders_StatusDropdown()
    {
        var project = new ProjectDetailDto
        {
            Id = 2,
            Name = "Project2",
            Status = ProjectStatus.Archived,
            Tags = []
        };

        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, project));

        Assert.Contains("Project2", cut.Markup);
    }

    [Fact]
    public void Renders_SaveButton()
    {
        var project = new ProjectDetailDto
        {
            Id = 3,
            Name = "Project3",
            Tags = []
        };

        _handler.SetJsonResponse("api/projects/3", project);

        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, project));

        // The submit button carries the localized "Save" text and the save icon.
        Assert.Contains(cut.FindAll("button"), b =>
            b.TextContent.Contains("Save") && b.InnerHtml.Contains("save"));
    }

    [Fact]
    public void Renders_DeleteButton()
    {
        var project = new ProjectDetailDto
        {
            Id = 4,
            Name = "Project4",
            Tags = ["tag1"]
        };

        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, project));

        // The Delete button carries the localized "Delete" text and the delete icon.
        Assert.Contains(cut.FindAll("button"), b =>
            b.TextContent.Contains("Delete") && b.InnerHtml.Contains("delete"));
    }

    [Fact]
    public void Renders_TagsField_WithValues()
    {
        var project = new ProjectDetailDto
        {
            Id = 5,
            Name = "Project5",
            Tags = ["frontend", "backend", "devops"]
        };

        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, project));

        Assert.Contains("frontend, backend, devops", cut.Markup);
    }
}
