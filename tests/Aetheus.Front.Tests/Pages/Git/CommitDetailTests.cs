// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Git;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Git;

public class CommitDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public CommitDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static GitCommitDto Sample(int id = 3, string? repoUrl = "https://localhost:5302/git/1/demo.git") => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Sha = "abcdef1234567890",
        RepositoryUrl = repoUrl
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
    public void ExistingCommit_RedirectsToCanonicalRepositoryCommit()
    {
        _handler.SetJsonResponse("api/gitgraph/commits/3", Sample());
        SetRepositories(new GitLightRepoDto
        {
            Id = 7,
            ProjectId = 1,
            Name = "Demo",
            CloneUrl = "https://localhost:5302/git/1/demo.git"
        });

        Render<CommitDetail>(parameters => parameters.Add(component => component.CommitId, 3));

        Assert.EndsWith("/git-repositories/7/commits/abcdef1234567890",
            Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void DifferentLauncherHost_StillMatchesRepositoryByPath()
    {
        _handler.SetJsonResponse("api/gitgraph/commits/4", Sample(4,
            "https://host.docker.internal:5303/git/1/demo.git"));
        SetRepositories(
            new GitLightRepoDto { Id = 7, ProjectId = 1, Name = "Demo", CloneUrl = "https://localhost:5302/git/1/demo.git" },
            new GitLightRepoDto { Id = 8, ProjectId = 1, Name = "Other", CloneUrl = "https://localhost:5302/git/1/other.git" });

        Render<CommitDetail>(parameters => parameters.Add(component => component.CommitId, 4));

        Assert.EndsWith("/git-repositories/7/commits/abcdef1234567890",
            Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void MissingCommit_DoesNotRedirect()
    {
        _handler.SetResponse("api/gitgraph/commits/9", HttpStatusCode.NotFound);

        var cut = Render<CommitDetail>(parameters => parameters.Add(component => component.CommitId, 9));

        cut.WaitForAssertion(() => Assert.Contains("NotFound", cut.Markup));
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
