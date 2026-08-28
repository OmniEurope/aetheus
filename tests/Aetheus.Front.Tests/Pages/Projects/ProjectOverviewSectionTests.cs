// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class ProjectOverviewSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectOverviewSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_NullProject_ShowsAetheusLoader()
    {
        var cut = Render<ProjectOverviewSection>(p => p.Add(x => x.Project, (ProjectDetailDto?)null));
        // Null project → only the branded loading indicator renders.
        Assert.Contains("aetheus-loader-logo", cut.Markup);
    }

    [Fact]
    public void Renders_WithRecentPipelinesAndCommits()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=1", new List<PipelineRunDto>
        {
            new()
            {
                Id = 42,
                PipelineId = 7,
                PipelineName = "Release",
                ProjectId = 1,
                Status = PipelineStatus.Success,
                StartedAt = new DateTime(2026, 1, 2, 10, 0, 0),
                Steps = [new PipelineStepRunDto { Id = 1, TriggeredRunId = 43 }]
            },
            new()
            {
                Id = 43,
                PipelineId = 8,
                PipelineName = "Deploy",
                ProjectId = 1,
                Status = PipelineStatus.Success,
                StartedAt = new DateTime(2026, 1, 2, 10, 1, 0)
            }
        });
        _handler.SetPaginatedJsonResponse(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=1&sortDescending=False",
            new List<GitLightRepoDto>
        {
            new() { Id = 3, ProjectId = 1, Name = "web", DefaultBranch = "main" }
        });
        _handler.SetJsonResponse("api/git/repos/3/commits", new PaginatedResult<GitLightCommitDto>
        {
            Items =
            [
                new()
                {
                    Sha = "abcdef123456",
                    ShortSha = "abcdef1",
                    Message = "Improve overview",
                    AuthorName = "Alice",
                    AuthorDate = new DateTime(2026, 1, 2, 9, 0, 0),
                    RefNames = ["develop"]
                }
            ],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        _handler.SetPaginatedJsonResponse("api/git/repos/3/tree", new List<GitLightTreeEntryDto>());

        var cut = Render<ProjectOverviewSection>(p =>
            p.Add(x => x.Project, new ProjectDetailDto
            {
                Id = 1,
                Name = "Web",
                Description = "A project",
                RepositoryUrl = "https://example/repo",
                DefaultBranch = "main",
                LatestGateGrade = AnalysisGrade.C
            }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("RecentPipelines", cut.Markup);
            Assert.Contains("Release", cut.Markup);
            Assert.Contains(">42<", cut.Markup);
            Assert.DoesNotContain("Deploy", cut.Markup);
            Assert.Contains("RecentCommits", cut.Markup);
            Assert.True(cut.Markup.IndexOf("RecentCommits", StringComparison.Ordinal) <
                        cut.Markup.IndexOf("RecentPipelines", StringComparison.Ordinal));
            Assert.Contains("Improve overview", cut.Markup);
            Assert.Contains("abcdef1", cut.Markup);
            Assert.Contains("Branch", cut.Markup);
            Assert.Contains("main", cut.Markup);
            Assert.Contains("develop", cut.Markup);
            Assert.Contains("project-overview-gate-grade analysis-grade-c", cut.Markup);
            Assert.Contains("href=\"/projects/1/quality\"", cut.Markup);
            Assert.DoesNotContain("RecentActivity", cut.Markup);
            Assert.Equal(5, cut.FindComponent<PipelineRunsGrid>().Instance.MaxGroups);
            Assert.Contains(_handler.Requests, request =>
                request.Method == "GET"
                && request.Url.Contains("api/git/repos/3/commits", StringComparison.Ordinal)
                && (request.Url.Contains("ref=%2A", StringComparison.OrdinalIgnoreCase)
                    || request.Url.Contains("ref=*", StringComparison.Ordinal)));
        });

        cut.Find("button[aria-label='ExpandLinkedPipelineRuns']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deploy", cut.Markup);
            Assert.Contains(">43<", cut.Markup);
        });
    }

    [Fact]
    public void Renders_WithProjectNoRecentData_ShowsBothEmptyHints()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=1", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=1&sortDescending=False", []);
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());

        var cut = Render<ProjectOverviewSection>(p =>
            p.Add(x => x.Project, new ProjectDetailDto { Id = 1, Name = "Web" }));

        Assert.Contains("Information", cut.Markup);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("NoRecentPipelines", cut.Markup);
            Assert.Contains("NoRecentCommits", cut.Markup);
            Assert.DoesNotContain("RecentActivity", cut.Markup);
            Assert.Contains("ProjectGettingStartedTitle", cut.Markup);
            Assert.Contains("project-overview-tabs", cut.Markup);
            Assert.Contains("Readme", cut.Markup);
            Assert.Contains("Changelog", cut.Markup);
            Assert.Single(cut.FindAll(".project-onboarding-grid"));
            Assert.Equal(3, cut.FindAll(".project-onboarding-link").Count);
            Assert.Equal(3, cut.FindAll(".project-onboarding-state").Count);
            Assert.Contains("git-repositories?projectId=1&amp;create=true", cut.Markup);
            Assert.Contains("environments/new?projectId=1", cut.Markup);
            Assert.Contains("pipelines/setup?projectId=1", cut.Markup);
            Assert.Equal(14, cut.FindAll(".project-section-link").Count);
            Assert.Equal(14, cut.FindAll(".project-section-tile").Count);
            Assert.Equal("page", cut.Find(".project-section-link[href='/projects/1/overview']").GetAttribute("aria-current"));
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/1/quality']"));
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/1/artifacts']"));
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/1/environments']"));
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/1/monitoring']"));
            Assert.NotNull(cut.Find(".project-section-link[href='/git-repositories?projectId=1']"));
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/1/edit']"));
        });

        cut.FindAll(".rz-tabview-nav button").Single(button => button.TextContent.Contains("Readme")).Click();
        cut.WaitForAssertion(() => Assert.Contains("NoProjectReadme", cut.Markup));
        cut.FindAll(".rz-tabview-nav button").Single(button => button.TextContent.Contains("Changelog")).Click();
        cut.WaitForAssertion(() => Assert.Contains("NoProjectChangelog", cut.Markup));
    }

    [Fact]
    public void Renders_ReadmeAndChangelog_InReadableSurfaces()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=4", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=4&sortDescending=False",
            new List<GitLightRepoDto>
            {
                new() { Id = 21, ProjectId = 4, Name = "docs", DefaultBranch = "main" }
            });
        _handler.SetJsonResponse("api/git/repos/21/commits", new PaginatedResult<GitLightCommitDto>());
        _handler.SetPaginatedJsonResponse("api/git/repos/21/tree", new List<GitLightTreeEntryDto>
        {
            new() { Name = "README.md", Path = "README.md", Type = GitTreeEntryType.Blob }
        });
        _handler.SetJsonResponse("api/git/repos/21/blob", new GitLightBlobDto
        {
            Path = "README.md",
            Content = "# Documentation\n\nReadable content",
            IsBinary = false
        });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items =
            [
                new ReleaseDto
                {
                    Id = 8,
                    ProjectId = 4,
                    Version = "2.0.0",
                    Status = ReleaseStatus.Published,
                    Changelog = "Improved navigation",
                    PublishedAt = new DateTime(2026, 8, 10, 10, 0, 0)
                }
            ]
        });

        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project,
            new ProjectDetailDto { Id = 4, Name = "Docs" }));

        cut.FindAll(".rz-tabview-nav button").Single(button => button.TextContent.Contains("Readme")).Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("project-readme-panel", cut.Markup);
            Assert.Contains("project-readme-content", cut.Markup);
            Assert.Contains("Documentation", cut.Markup);
        });
        cut.FindAll(".rz-tabview-nav button").Single(button => button.TextContent.Contains("Changelog")).Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("project-changelog-entry", cut.Markup);
            Assert.Contains("Improved navigation", cut.Markup);
        });
    }

    [Fact]
    public void Renders_InitializedProject_HidesGettingStarted()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=2", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=2&sortDescending=False",
            new List<GitLightRepoDto>
            {
                new() { Id = 5, ProjectId = 2, Name = "api", DefaultBranch = "main", IsEmpty = true }
            });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        _handler.SetPaginatedJsonResponse("api/git/repos/5/tree", new List<GitLightTreeEntryDto>());

        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project,
            new ProjectDetailDto
            {
                Id = 2,
                Name = "API",
                EnvironmentCount = 1,
                Pipelines = [new PipelineDto { Id = 9, Name = "CI" }]
            }));

        cut.WaitForAssertion(() => Assert.Contains("NoRecentCommits", cut.Markup));
        Assert.DoesNotContain("ProjectGettingStartedTitle", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AddGitRepositoryButton_OpensProjectScopedCreationFlow()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=2", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=2&sortDescending=False", []);
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project,
            new ProjectDetailDto { Id = 2, Name = "API" }));

        cut.WaitForAssertion(() => Assert.Contains("AddGitRepository", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.Contains("AddGitRepository")).Click();

        Assert.EndsWith("/git-repositories?projectId=2&create=true",
            Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionsLoadedAfterMount_RevealsAddGitRepositoryButton()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=2", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=2&sortDescending=False", []);
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        var permissions = Services.GetRequiredService<Aetheus.Front.Services.PermissionService>();
        permissions.Clear();

        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project,
            new ProjectDetailDto { Id = 2, Name = "API" }));

        Assert.DoesNotContain("AddGitRepository", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(7, cut.FindAll(".project-section-link").Count);

        permissions.SetPermissions(
        [
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 2,
                Permission = Permission.Write
            }
        ], isAdmin: false);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AddGitRepository", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(8, cut.FindAll(".project-section-link").Count);
            Assert.NotNull(cut.Find(".project-section-link[href='/projects/2/edit']"));
            Assert.Empty(cut.FindAll(".project-section-link[href='/projects/2/pipelines']"));
        });
    }

    [Fact]
    public void Renders_InternalRepositoryAsLinkedRepository()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=3", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=3&sortDescending=False",
            new List<GitLightRepoDto>
            {
                new()
                {
                    Id = 17,
                    ProjectId = 3,
                    Name = "portfolio",
                    DefaultBranch = "main",
                    CloneUrl = "https://git.example/portfolio.git"
                }
            });
        _handler.SetJsonResponse("api/git/repos/17/commits", new PaginatedResult<GitLightCommitDto>());
        _handler.SetPaginatedJsonResponse("api/git/repos/17/tree", new List<GitLightTreeEntryDto>());
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());

        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project,
            new ProjectDetailDto { Id = 3, Name = "Portfolio" }));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("NoRepository", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("href=\"/git-repositories/17\"", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("portfolio", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("https://git.example/portfolio.git", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("main", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task PipelineRunStarted_ForProjectPipeline_ReloadsRecentRuns()
    {
        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=1", new List<PipelineRunDto>());
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=1&sortDescending=False", []);
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        var project = new ProjectDetailDto
        {
            Id = 1,
            Name = "Web",
            Pipelines = [new PipelineDto { Id = 7, Name = "Release" }]
        };
        var cut = Render<ProjectOverviewSection>(parameters => parameters.Add(component => component.Project, project));
        cut.WaitForAssertion(() => Assert.Contains("NoRecentPipelines", cut.Markup));

        _handler.SetJsonResponse("api/pipelines/runs/recent?projectId=1", new List<PipelineRunDto>
        {
            new() { Id = 43, PipelineId = 7, ProjectId = 1, PipelineName = "Release", Status = PipelineStatus.Running }
        });
        var method = typeof(ProjectOverviewSection).GetMethod("OnPipelineRunStarted", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [43, 7])!);

        cut.WaitForAssertion(() => Assert.Contains(">43<", cut.Markup), TimeSpan.FromSeconds(2));
    }
}
