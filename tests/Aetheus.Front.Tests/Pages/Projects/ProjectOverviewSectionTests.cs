// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class ProjectOverviewSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectOverviewSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_NullProject_ShowsSpinner()
    {
        var cut = Render<ProjectOverviewSection>(p => p.Add(x => x.Project, (ProjectDetailDto?)null));
        // Null project → only the loading spinner renders.
        Assert.Contains("rz-progressbar-circular", cut.Markup);
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
                StartedAt = new DateTime(2026, 1, 2, 10, 0, 0)
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
                    AuthorDate = new DateTime(2026, 1, 2, 9, 0, 0)
                }
            ],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        _handler.SetPaginatedJsonResponse("api/git/repos/3/tree", new List<GitLightTreeEntryDto>());

        var cut = Render<ProjectOverviewSection>(p =>
            p.Add(x => x.Project, new ProjectDetailDto { Id = 1, Name = "Web", Description = "A project", RepositoryUrl = "https://example/repo", DefaultBranch = "main" }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("RecentPipelines", cut.Markup);
            Assert.Contains("Release", cut.Markup);
            Assert.Contains("#42", cut.Markup);
            Assert.Contains("RecentCommits", cut.Markup);
            Assert.True(cut.Markup.IndexOf("RecentCommits", StringComparison.Ordinal) <
                        cut.Markup.IndexOf("RecentPipelines", StringComparison.Ordinal));
            Assert.Contains("Improve overview", cut.Markup);
            Assert.Contains("abcdef1", cut.Markup);
            Assert.Contains("Branch", cut.Markup);
            Assert.Contains("main", cut.Markup);
            Assert.DoesNotContain("RecentActivity", cut.Markup);
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

        cut.WaitForAssertion(() => Assert.Contains("#43", cut.Markup), TimeSpan.FromSeconds(2));
    }
}
