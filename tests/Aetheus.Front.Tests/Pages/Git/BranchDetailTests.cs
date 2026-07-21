// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Git;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Git;

public class BranchDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public BranchDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static GitBranchDto Sample(int id = 3, string? repoUrl = "https://github.com/acme/demo.git") => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Name = "feature/awesome",
        RepositoryUrl = repoUrl
    };

    [Fact]
    public void Renders_LoadedBranch_ShowsName()
    {
        _handler.SetJsonResponse("api/gitgraph/branches/3", Sample());

        var cut = Render<BranchDetail>(p => p.Add(c => c.BranchId, 3));

        cut.WaitForState(() => cut.Markup.Contains("feature/awesome"));
        Assert.Contains("feature/awesome", cut.Markup);
    }

    [Fact]
    public void Renders_LocalRepository_StillRendersBranch()
    {
        _handler.SetJsonResponse("api/gitgraph/branches/4", Sample(4, repoUrl: "/srv/git/demo"));

        var cut = Render<BranchDetail>(p => p.Add(c => c.BranchId, 4));

        cut.WaitForState(() => cut.Markup.Contains("feature/awesome"));
        Assert.Contains("feature/awesome", cut.Markup);
    }

    [Fact]
    public void Renders_NotFound_DoesNotShowBranchName()
    {
        _handler.SetResponse("api/gitgraph/branches/9", HttpStatusCode.NotFound);

        var cut = Render<BranchDetail>(p => p.Add(c => c.BranchId, 9));

        Assert.DoesNotContain("feature/awesome", cut.Markup);
    }

    [Fact]
    public void BranchIdChange_ReloadsSameComponentInstance()
    {
        _handler.SetJsonResponse("api/gitgraph/branches/1", Sample(1) with { Name = "first" });
        _handler.SetJsonResponse("api/gitgraph/branches/2", Sample(2) with { Name = "second" });
        var cut = Render<BranchDetail>(p => p.Add(c => c.BranchId, 1));

        cut.Render(p => p.Add(c => c.BranchId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second", cut.Markup));
        Assert.DoesNotContain(">first<", cut.Markup);
    }
}
