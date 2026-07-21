// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class GitTagCreateDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitTagCreateDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_FormFields()
    {
        var branches = new List<GitLightBranchDto>
        {
            new() { Name = "main" }
        };
        var cut = Render<GitTagCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, branches));

        Assert.Contains("TagName", cut.Markup);
        Assert.Contains("PointsTo", cut.Markup);
        Assert.Contains("Message", cut.Markup);
    }

    [Fact]
    public void Renders_Buttons()
    {
        var cut = Render<GitTagCreateDialog>(p => p
            .Add(x => x.RepoId, 1)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        Assert.Contains("Create", cut.Markup);
        Assert.Contains("Cancel", cut.Markup);
    }

    [Fact]
    public void Renders_WithBranches()
    {
        var branches = new List<GitLightBranchDto>
        {
            new() { Name = "main" },
            new() { Name = "develop" },
            new() { Name = "release/v1.0" }
        };
        var cut = Render<GitTagCreateDialog>(p => p
            .Add(x => x.RepoId, 2)
            .Add(x => x.Branches, branches));

        // The form fields render and the branches feed the PointsTo selector.
        Assert.Contains("TagName", cut.Markup);
        Assert.Contains("PointsTo", cut.Markup);
        Assert.Equal(3, cut.Instance.Branches.Count);
    }

    [Fact]
    public async Task Submit_PostsCreateRequestToTagsEndpoint()
    {
        _handler.SetResponse(
            HttpMethod.Post,
            "api/git/repos/4/tags",
            System.Net.HttpStatusCode.NoContent);
        var cut = Render<GitTagCreateDialog>(p => p
            .Add(x => x.RepoId, 4)
            .Add(x => x.Branches, new List<GitLightBranchDto>()));

        var request = typeof(GitTagCreateDialog)
            .GetField("_request", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        request.GetType().GetProperty("Name")!.SetValue(request, "v1.0.0");

        var submit = typeof(GitTagCreateDialog).GetMethod("Submit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)submit.Invoke(cut.Instance, [])!);

        // Submit POSTs the create request to the repo's tags endpoint. (The dialog-close-with-true on
        // success is a DialogService.Close a standalone bUnit render cannot observe.)
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/git/repos/4/tags"));
    }
}
