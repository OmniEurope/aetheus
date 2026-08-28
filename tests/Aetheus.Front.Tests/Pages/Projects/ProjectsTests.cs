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
    public void ProjectCard_RendersCommitRunProductionAndParentLinks()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items =
            [
                new ProjectDto
                {
                    Id = 1,
                    Name = "Operational",
                    Status = ProjectStatus.Active,
                    LastCommitId = 42,
                    LastCommitSha = "0123456789abcdef",
                    LastCommitMessage = "Ship project cards",
                    LastCommitAt = new DateTime(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc),
                    LastRunId = 52,
                    LastRunName = "Deploy",
                    LastRunStatus = PipelineStatus.Success,
                    LastRunAt = new DateTime(2026, 7, 30, 9, 5, 0, DateTimeKind.Utc),
                    ParentRunId = 51,
                    ParentRunName = "Release",
                    LatestGateGrade = AnalysisGrade.B,
                    ProductionStatus = ProjectProductionStatus.Online,
                    OnlineUserCount = 8
                }
            ],
            TotalCount = 1
        });
        var cut = Render<ProjectsPage>();
        cut.WaitForState(() => cut.Markup.Contains("Operational"), TimeSpan.FromSeconds(2));

        Assert.Contains("href=\"/git-repositories/commits/42\"", cut.Markup);
        Assert.Contains("href=\"/pipelines/runs/52\"", cut.Markup);
        Assert.Contains("href=\"/pipelines/runs/51\"", cut.Markup);
        Assert.Contains("project-production-status online", cut.Markup);
        Assert.Contains("project-online-users", cut.Markup);
        Assert.Contains("project-gate-grade analysis-grade-b", cut.Markup);
        Assert.Contains("href=\"/projects/1/quality\"", cut.Markup);
        Assert.Contains("OnlineUsers", cut.Markup);
        Assert.DoesNotContain("PipelineCount", cut.Markup);
        Assert.DoesNotContain("Repository", cut.Markup);
    }

    [Fact]
    public void ProjectCommit_DeepLinksToItsInternalRepositoryWhenKnown()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items =
            [
                new ProjectDto
                {
                    Id = 1,
                    Name = "Repository project",
                    Status = ProjectStatus.Active,
                    InternalRepositoryId = 7,
                    LastCommitId = 42,
                    LastCommitSha = "0123456789abcdef"
                }
            ],
            TotalCount = 1
        });

        var cut = Render<ProjectsPage>();

        cut.WaitForAssertion(() =>
            Assert.Contains("href=\"/git-repositories/7/commits/0123456789abcdef\"", cut.Markup));
    }

    [Fact]
    public void DenseView_SwitchesToCompactPortfolioList()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "Dense", Status = ProjectStatus.Active }],
            TotalCount = 1
        });

        var cut = Render<ProjectsPage>();
        cut.WaitForState(() => cut.Markup.Contains("Dense"), TimeSpan.FromSeconds(2));
        cut.Find("button[title='DenseView']").Click();

        Assert.Contains("project-dense-list", cut.Markup);
        Assert.DoesNotContain("project-card-grid\"", cut.Markup);
    }

    [Fact]
    public void FavoriteButton_PinsProjectAndPersistsPreference()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 7, Name = "Favorite me", Status = ProjectStatus.Active }],
            TotalCount = 1
        });

        var cut = Render<ProjectsPage>();
        cut.WaitForState(() => cut.Markup.Contains("Favorite me"), TimeSpan.FromSeconds(2));
        cut.Find("button[title='AddToFavorites']").Click();

        Assert.Contains("project-favorite active", cut.Markup);
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "localStorage.setItem"
            && Equals(invocation.Arguments[0], "aetheus.projects.favorites"));
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

