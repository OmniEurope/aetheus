// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Git;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages.Git;

public class GitDefaultBranchButtonTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitDefaultBranchButtonTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Click_UpdatesDefaultBranchAndNotifiesParent()
    {
        _handler.SetJsonResponse(HttpMethod.Put, "api/git/repos/1", new GitLightRepoDto
        {
            Id = 1,
            DefaultBranch = "main"
        });
        var changed = false;
        var cut = Render<GitDefaultBranchButton>(parameters => parameters
            .Add(component => component.RepositoryId, 1)
            .Add(component => component.Branch, new GitLightBranchDto { Name = "main" })
            .Add(component => component.CanWrite, true)
            .Add(component => component.Changed, EventCallback.Factory.Create(this, () => changed = true)));

        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.True(changed));

        Assert.Contains(_handler.Requests, request =>
            request.Method == "PUT" && request.Url.Contains("api/git/repos/1", StringComparison.Ordinal));
    }
}
