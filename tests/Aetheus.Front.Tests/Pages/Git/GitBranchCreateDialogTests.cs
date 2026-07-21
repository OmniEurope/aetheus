// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class GitBranchCreateDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitBranchCreateDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_FormFields()
    {
        var branches = new List<GitLightBranchDto>
        {
            new() { Name = "main" },
            new() { Name = "develop" }
        };
        var cut = Render<GitBranchCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, branches));

        Assert.Contains("BranchName", cut.Markup);
        Assert.Contains("StartFrom", cut.Markup);
    }

    [Fact]
    public void Renders_Buttons()
    {
        var cut = Render<GitBranchCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        Assert.Contains("Create", cut.Markup);
        Assert.Contains("Cancel", cut.Markup);
    }

    [Fact]
    public void Renders_WithEmptyBranches()
    {
        var cut = Render<GitBranchCreateDialog>(p => p
            .Add(x => x.RepoId, 3)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        // The form renders the branch-name and start-point fields with no source branches.
        Assert.Contains("BranchName", cut.Markup);
        Assert.Contains("StartFrom", cut.Markup);
        Assert.Empty(cut.Instance.Branches);
    }

    [Fact]
    public async Task Submit_PostsCreateRequestToBranchesEndpoint()
    {
        _handler.SetResponse(HttpMethod.Post, "api/git/repos/7/branches", System.Net.HttpStatusCode.OK);

        var cut = Render<GitBranchCreateDialog>(p => p
            .Add(x => x.RepoId, 7)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        // Fill the bound request with a valid branch name, then run Submit.
        var request = typeof(GitBranchCreateDialog)
            .GetField("_request", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        request.GetType().GetProperty("Name")!.SetValue(request, "feature/x");

        var submit = typeof(GitBranchCreateDialog).GetMethod("Submit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)submit.Invoke(cut.Instance, [])!);

        // Submit POSTs the create request to the repo's branches endpoint. (The dialog-close-with-true on
        // success is a DialogService.Close a standalone bUnit render cannot observe - Radzen only fires
        // OnClose for a dialog opened through its stack - so we assert the real HTTP side effect.)
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/git/repos/7/branches"));
    }
}
