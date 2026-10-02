// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Components.Git;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Git;

public class BranchDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public BranchDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static GitBranchDto Sample(int id = 3) => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Name = "feature/awesome",
        RepositoryUrl = "https://localhost:5302/git/1/demo.git"
    };

    private void SetRepositories(params GitLightRepoDto[] repositories) =>
        _handler.SetJsonResponse("api/git/repos", new PaginatedResult<GitLightRepoDto>
        {
            Items = repositories.ToList(),
            TotalCount = repositories.Length,
            Page = 1,
            PageSize = 100
        });

    [Fact]
    public void ExistingBranch_RedirectsToCanonicalRepositoryBranchesTab()
    {
        _handler.SetJsonResponse("api/gitgraph/branches/3", Sample());
        SetRepositories(new GitLightRepoDto
        {
            Id = 7,
            ProjectId = 1,
            Name = "Demo",
            CloneUrl = "https://localhost:5302/git/1/demo.git"
        });

        Render<BranchDetail>(parameters => parameters.Add(component => component.BranchId, 3));

        Assert.EndsWith("/git-repositories/7?tab=branches&branch=feature%2Fawesome",
            Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void MissingBranch_DoesNotRedirect()
    {
        _handler.SetResponse("api/gitgraph/branches/9", HttpStatusCode.NotFound);

        var cut = Render<BranchDetail>(parameters => parameters.Add(component => component.BranchId, 9));

        cut.WaitForAssertion(() => Assert.Contains("NotFound", cut.Markup));
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
