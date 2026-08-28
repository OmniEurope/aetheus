// SPDX-License-Identifier: EUPL-1.2
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
    public void InputEvents_AreSentByTheCreateForm()
    {
        _handler.SetJsonResponse("api/git/repos", new GitLightRepoDto { Id = 9, Name = "new-repo", ProjectId = 42 });

        var cut = Render<GitRepositoryCreateDialog>(p => p.Add(x => x.ProjectId, 42));

        cut.Find("input[name='Name']").Input("new-repo");
        cut.Find("textarea[name='Description']").Input("Local Portfolio test repository.");
        cut.Find("input[name='DefaultBranch']").Input("develop");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(
            _handler.Requests,
            request => request.Method == "POST" && request.Url.EndsWith("api/git/repos", StringComparison.Ordinal)));
        var body = _handler.RequestDetails.Last(request =>
            request.Method == "POST" && request.Url.EndsWith("api/git/repos", StringComparison.Ordinal)).Body;
        var request = System.Text.Json.JsonSerializer.Deserialize<CreateGitLightRepoRequest>(
            body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(42, request!.ProjectId);
        Assert.Equal("new-repo", request.Name);
        Assert.Equal("Local Portfolio test repository.", request.Description);
        Assert.Equal("develop", request.DefaultBranch);
    }
}
