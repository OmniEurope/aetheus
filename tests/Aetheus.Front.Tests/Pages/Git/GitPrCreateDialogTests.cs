// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class GitPrCreateDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitPrCreateDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_FormFields()
    {
        var branches = new List<GitLightBranchDto>
        {
            new() { Name = "main" },
            new() { Name = "feature/test" }
        };
        var cut = Render<GitPrCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, branches));

        Assert.Contains("Title", cut.Markup);
        Assert.Contains("SourceBranch", cut.Markup);
        Assert.Contains("TargetBranch", cut.Markup);
        Assert.Contains("Description", cut.Markup);
    }

    [Fact]
    public void Renders_CreateButton()
    {
        var cut = Render<GitPrCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        Assert.Contains("Create", cut.Markup);
        Assert.Contains("GoBack", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyBranches()
    {
        var cut = Render<GitPrCreateDialog>(p => p
            .Add(x => x.RepoId, 5)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        // The PR form renders its branch selectors even with no branches available.
        Assert.Contains("SourceBranch", cut.Markup);
        Assert.Contains("TargetBranch", cut.Markup);
        Assert.Empty(cut.Instance.Branches);
    }

    [Fact]
    public async Task Submit_PostsCreateRequestToPullRequestsEndpoint()
    {
        _handler.SetJsonResponse("api/git/repos/6/pull-requests",
            new InternalPullRequestDto { Id = 1, Number = 12, Title = "Add feature", SourceBranch = "feature/x", TargetBranch = "main" });

        var cut = Render<GitPrCreateDialog>(p => p
            .Add(x => x.RepoId, 6)
            .Add(x => x.Branches, new List<GitLightBranchDto>
            {
                new() { Name = "main" },
                new() { Name = "feature/x" }
            }));

        var request = typeof(GitPrCreateDialog)
            .GetField("_request", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        request.GetType().GetProperty("SourceBranch")!.SetValue(request, "feature/x");
        request.GetType().GetProperty("TargetBranch")!.SetValue(request, "main");
        request.GetType().GetProperty("Title")!.SetValue(request, "Add feature");

        var submit = typeof(GitPrCreateDialog).GetMethod("Submit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)submit.Invoke(cut.Instance, [])!);

        // Submit POSTs the create request to the repo's pull-requests endpoint. (The dialog-close-with-true
        // on success is a OmniDialogService.Close a standalone bUnit render cannot observe.)
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/git/repos/6/pull-requests"));
    }
}
