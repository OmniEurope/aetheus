// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class GitRepositoryCreateDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitRepositoryCreateDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_FormFields()
    {
        var cut = Render<GitRepositoryCreateDialog>(p => p.Add(x => x.ProjectId, 1));

        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Description", cut.Markup);
        Assert.Contains("DefaultBranch", cut.Markup);
    }

    [Fact]
    public void Renders_Buttons()
    {
        var cut = Render<GitRepositoryCreateDialog>(p => p.Add(x => x.ProjectId, 1));

        Assert.Contains("Create", cut.Markup);
        Assert.Contains("Cancel", cut.Markup);
    }

    [Fact]
    public void Renders_WithProjectId()
    {
        var cut = Render<GitRepositoryCreateDialog>(p => p.Add(x => x.ProjectId, 42));

        // The create form renders its fields for the given project.
        Assert.Contains("Name", cut.Markup);
        Assert.Contains("DefaultBranch", cut.Markup);
        Assert.Equal(42, cut.Instance.ProjectId);
    }

    [Fact]
    public async Task Submit_PostsCreateRequestToReposEndpoint()
    {
        _handler.SetJsonResponse("api/git/repos", new GitLightRepoDto { Id = 9, Name = "new-repo", ProjectId = 42 });

        var cut = Render<GitRepositoryCreateDialog>(p => p.Add(x => x.ProjectId, 42));

        var request = typeof(GitRepositoryCreateDialog)
            .GetField("_request", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        request.GetType().GetProperty("Name")!.SetValue(request, "new-repo");

        var submit = typeof(GitRepositoryCreateDialog).GetMethod("Submit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)submit.Invoke(cut.Instance, [])!);

        // Submit POSTs the create request to api/git/repos. (The dialog-close-with-true on a non-null
        // created repo is a DialogService.Close a standalone bUnit render cannot observe.)
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/git/repos"));
    }
}
