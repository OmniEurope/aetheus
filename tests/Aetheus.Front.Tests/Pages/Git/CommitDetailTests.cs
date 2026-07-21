// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Git;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Git;

public class CommitDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public CommitDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static GitCommitDto Sample(int id = 3, string? repoUrl = "https://github.com/acme/demo.git") => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Sha = "abcdef1234567890",
        Message = "feat: initial commit",
        Author = "alice",
        CommittedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
        RepositoryUrl = repoUrl
    };

    [Fact]
    public void Renders_LoadedCommit_ShowsShortSha()
    {
        _handler.SetJsonResponse("api/gitgraph/commits/3", Sample());

        var cut = Render<CommitDetail>(p => p.Add(c => c.CommitId, 3));

        cut.WaitForState(() => cut.Markup.Contains("abcdef12"));
        Assert.Contains("abcdef12", cut.Markup);
    }

    [Fact]
    public void Renders_LocalRepository_StillRendersCommit()
    {
        _handler.SetJsonResponse("api/gitgraph/commits/4", Sample(4, repoUrl: "/srv/git/demo"));

        var cut = Render<CommitDetail>(p => p.Add(c => c.CommitId, 4));

        cut.WaitForState(() => cut.Markup.Contains("abcdef12"));
        Assert.Contains("abcdef12", cut.Markup);
    }

    [Fact]
    public void Renders_NotFound_DoesNotShowSha()
    {
        _handler.SetResponse("api/gitgraph/commits/9", HttpStatusCode.NotFound);

        var cut = Render<CommitDetail>(p => p.Add(c => c.CommitId, 9));

        Assert.DoesNotContain("abcdef12", cut.Markup);
    }

    [Fact]
    public void CommitIdChange_ReloadsSameComponentInstance()
    {
        _handler.SetJsonResponse("api/gitgraph/commits/1", Sample(1) with { Message = "first message" });
        _handler.SetJsonResponse("api/gitgraph/commits/2", Sample(2) with { Message = "second message" });
        var cut = Render<CommitDetail>(p => p.Add(c => c.CommitId, 1));

        cut.Render(p => p.Add(c => c.CommitId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second message", cut.Markup));
        Assert.DoesNotContain("first message", cut.Markup);
    }
}
