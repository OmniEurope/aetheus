// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Pages.Releases;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Releases;

public class ReleaseDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ReleaseDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static ReleaseDto Sample(int id = 7) => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Version = "v2.3.0",
        BranchName = "main",
        DetectedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        PublishedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        BuildNumber = 128,
        CommitHash = "abcdef1234567890",
        TagName = "v2.3.0",
        RepositoryUrl = "https://github.com/acme/demo.git",
        Changelog = "- feat: add dashboard\n- fix: null crash\n- chore: bump deps"
    };

    [Fact]
    public void Renders_LoadedRelease_ShowsVersion()
    {
        _handler.SetJsonResponse("api/releases/7", Sample());

        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 7));

        cut.WaitForState(() => cut.Markup.Contains("v2.3.0"));
        Assert.Contains("v2.3.0", cut.Markup);
    }

    [Fact]
    public void Renders_NotFound_DoesNotShowVersion()
    {
        _handler.SetResponse("api/releases/9", HttpStatusCode.NotFound);

        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 9));

        Assert.DoesNotContain("v2.3.0", cut.Markup);
    }

    [Fact]
    public void ReleaseIdChange_RequestsSecondReleaseOnSameInstance()
    {
        _handler.SetResponse("api/releases/1", HttpStatusCode.NotFound);
        _handler.SetResponse("api/releases/2", HttpStatusCode.NotFound);
        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 1));

        cut.Render(p => p.Add(c => c.ReleaseId, 2));

        Assert.Contains(_handler.Requests, request => request.Url.Contains("api/releases/1"));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("api/releases/2"));
    }

    [Fact]
    public void Panels_AlwaysRenderSourceAndOnlyRenderDeliverablesWhenPresent()
    {
        _handler.SetJsonResponse("api/releases/7", Sample());
        _handler.SetJsonResponse("api/releases/8", Sample(8) with
        {
            EnvironmentName = "production",
            Artifacts =
            [
                new ArtifactLinkDto
                {
                    Id = 41,
                    Name = "release.zip",
                    SizeBytes = 2048
                }
            ]
        });

        var withoutDeliverables = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 7));
        withoutDeliverables.WaitForAssertion(() =>
        {
            Assert.Contains("Source", withoutDeliverables.Markup);
            Assert.DoesNotContain("Deliverables", withoutDeliverables.Markup);
        });

        var withDeliverables = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 8));
        withDeliverables.WaitForAssertion(() =>
        {
            Assert.Contains("Source", withDeliverables.Markup);
            Assert.Contains("Deliverables", withDeliverables.Markup);
            Assert.Contains("release.zip", withDeliverables.Markup);
            Assert.Contains("production", withDeliverables.Markup);
        });
    }

    [Fact]
    public void GitProvenanceLinks_UseOnlyCanonicalGitSectionRoutes()
    {
        _handler.SetJsonResponse("api/releases/7", Sample() with
        {
            Branches = [new BranchLinkDto { Id = 11, Name = "main" }],
            Commits = [new CommitLinkDto { Id = 22, Sha = "abcdef1234567890" }]
        });

        var cut = Render<ReleaseDetail>(parameters => parameters.Add(component => component.ReleaseId, 7));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("href=\"/git-repositories/branches/11\"", cut.Markup);
            Assert.Contains("href=\"/git-repositories/commits/22\"", cut.Markup);
            Assert.DoesNotContain("href=\"/git/", cut.Markup);
        });
    }

    [Fact]
    public void EmptyExplicitChangelog_FallsBackToLinkedCommitMessages()
    {
        _handler.SetJsonResponse("api/releases/7", Sample() with
        {
            Changelog = null,
            Commits = [new CommitLinkDto { Id = 22, Sha = "abcdef1234567890", Message = "fix: repair installer" }]
        });

        var cut = Render<ReleaseDetail>(parameters => parameters.Add(component => component.ReleaseId, 7));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Changelog", cut.Markup);
            Assert.Contains("repair installer", cut.Markup);
        });
    }

    [Fact]
    public void EmptyCommitLinkMessages_LoadInOneBatchForChangelog()
    {
        _handler.SetJsonResponse("api/releases/7", Sample() with
        {
            Changelog = null,
            RepositoryUrl = "https://example.test/repo.git",
            Commits =
            [
                new CommitLinkDto { Id = 22, Sha = "abcdef1234567890" },
                new CommitLinkDto { Id = 23, Sha = "1234567890abcdef" }
            ]
        });
        _handler.SetPaginatedJsonResponse("api/git/repos", new List<GitLightRepoDto>
        {
            new() { Id = 17, ProjectId = 1, CloneUrl = "https://example.test/repo.git" }
        });
        _handler.SetJsonResponse(HttpMethod.Post, "api/git/repos/17/commit-messages", new Dictionary<string, string>
        {
            ["abcdef1234567890"] = "feat: show linked commit changelog",
            ["1234567890abcdef"] = "fix: batch lookup"
        });

        var cut = Render<ReleaseDetail>(parameters => parameters.Add(component => component.ReleaseId, 7));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("show linked commit changelog", cut.Markup);
            Assert.Contains("batch lookup", cut.Markup);
            Assert.Single(_handler.Requests, request =>
                request.Method == "POST" && request.Url.Contains("commit-messages", StringComparison.Ordinal));
            Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("api/gitgraph/commits/", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ReleaseStatusChanged_UpdatesMatchingReleaseWithoutReload()
    {
        _handler.SetJsonResponse("api/releases/7", Sample() with { Status = ReleaseStatus.Building });
        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 7));
        cut.WaitForAssertion(() => Assert.Contains("Building", cut.Markup));
        var requestCount = _handler.Requests.Count;
        await cut.InvokeAsync(() =>
            cut.Instance.OnReleaseChanged(Sample() with { Status = ReleaseStatus.Deployed }));

        cut.WaitForAssertion(() => Assert.Contains("Deployed", cut.Markup));
        Assert.Equal(requestCount, _handler.Requests.Count);
    }
}
