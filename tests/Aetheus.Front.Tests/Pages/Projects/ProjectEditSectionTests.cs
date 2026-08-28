// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;
using Radzen.Blazor;

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
    public void Renders_AetheusLoader_WhenNoProject()
    {
        var cut = Render<ProjectEditSection>(p => p
            .Add(x => x.Project, null));

        // With Project null the section shows the loading placeholder, not the edit form.
        Assert.Contains("aetheus-loader-logo", cut.Markup);
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

    [Fact]
    public void InternalRepository_PopulatesDefaultBranchDropdown()
    {
        var project = new ProjectDetailDto
        {
            Id = 6,
            Name = "BranchedProject",
            DefaultBranch = "develop",
            Tags = []
        };
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=6",
            [new GitLightRepoDto { Id = 12, ProjectId = 6, Name = "repo", DefaultBranch = "main" }]);
        _handler.SetPaginatedJsonResponse(
            "api/git/repos/12/branches",
            new[]
            {
                new GitLightBranchDto { Name = "main", IsDefault = true },
                new GitLightBranchDto { Name = "develop" },
                new GitLightBranchDto { Name = "release/next" }
            });

        var cut = Render<ProjectEditSection>(parameters =>
            parameters.Add(component => component.Project, project));

        cut.WaitForAssertion(() =>
        {
            var branchDropdown = Assert.Single(cut.FindComponents<RadzenDropDown<string>>());
            Assert.Equal(
                ["main", "develop", "release/next"],
                Assert.IsAssignableFrom<IEnumerable<string>>(branchDropdown.Instance.Data));
            Assert.Equal("develop", branchDropdown.Instance.Value);
        });
    }

    [Fact]
    public void MultipleInternalRepositories_DoNotChooseAnArbitraryBranchSource()
    {
        var project = new ProjectDetailDto
        {
            Id = 7,
            Name = "AmbiguousProject",
            DefaultBranch = "release/custom",
            Tags = []
        };
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=7",
            [
                new GitLightRepoDto { Id = 21, ProjectId = 7, Name = "first", DefaultBranch = "main" },
                new GitLightRepoDto { Id = 22, ProjectId = 7, Name = "second", DefaultBranch = "develop" }
            ]);

        var cut = Render<ProjectEditSection>(parameters =>
            parameters.Add(component => component.Project, project));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindComponents<RadzenDropDown<string>>());
            Assert.Contains("release/custom", cut.Markup);
        });
        Assert.DoesNotContain(
            _handler.Requests,
            request => request.Url.Contains("/branches", StringComparison.Ordinal));
    }

    [Fact]
    public void SameProjectRerender_PreservesBranchBeingEdited()
    {
        var project = new ProjectDetailDto
        {
            Id = 8,
            Name = "StableProject",
            DefaultBranch = "main",
            Tags = []
        };

        var cut = Render<ProjectEditSection>(parameters =>
            parameters.Add(component => component.Project, project));
        cut.Find("input[name='DefaultBranch']").Change("develop");

        cut.Render(parameters => parameters.Add(component => component.Project, project));

        Assert.Equal("develop", cut.Find("input[name='DefaultBranch']").GetAttribute("value"));
    }

    [Fact]
    public void DifferentProjectAndRepositoryDefaults_RenderExplicitWarning()
    {
        var project = new ProjectDetailDto
        {
            Id = 9,
            Name = "MismatchProject",
            DefaultBranch = "develop",
            Tags = []
        };
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=9",
            [new GitLightRepoDto { Id = 90, ProjectId = 9, Name = "repo", DefaultBranch = "main" }]);
        _handler.SetPaginatedJsonResponse(
            "api/git/repos/90/branches",
            new[]
            {
                new GitLightBranchDto { Name = "main", IsDefault = true },
                new GitLightBranchDto { Name = "develop" }
            });

        var cut = Render<ProjectEditSection>(parameters =>
            parameters.Add(component => component.Project, project));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ProjectGitDefaultBranchMismatch", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("develop", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("main", cut.Markup, StringComparison.Ordinal);
        });
    }
}
