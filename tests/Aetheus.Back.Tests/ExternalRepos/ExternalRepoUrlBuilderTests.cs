// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.ExternalRepos;

public class ExternalRepoUrlBuilderTests
{
    [Fact]
    public void GitHub_HttpsToken_BuildsCanonicalHttpsUrl()
    {
        var url = ExternalRepoUrlBuilder.Build(GitProviderType.GitHub, null, "octocat", "hello-world", GitAuthType.HttpsToken);
        Assert.Equal("https://github.com/octocat/hello-world.git", url);
    }

    [Fact]
    public void GitLab_Ssh_BuildsScpStyleUrl()
    {
        var url = ExternalRepoUrlBuilder.Build(GitProviderType.GitLab, null, "group", "proj", GitAuthType.Ssh);
        Assert.Equal("git@gitlab.com:group/proj.git", url);
    }

    [Fact]
    public void SelfHosted_BaseUrlAsFullUrl_UsesHostOnly()
    {
        var url = ExternalRepoUrlBuilder.Build(GitProviderType.Gitea, "https://git.example.com/", "team", "svc", GitAuthType.HttpsToken);
        Assert.Equal("https://git.example.com/team/svc.git", url);
    }

    [Fact]
    public void StripsTrailingDotGitAndSlashes()
    {
        var url = ExternalRepoUrlBuilder.Build(GitProviderType.GitHub, null, "/owner/", "repo.git", GitAuthType.HttpsToken);
        Assert.Equal("https://github.com/owner/repo.git", url);
    }

    [Fact]
    public void Gitea_WithoutBaseUrl_Throws()
    {
        Assert.Throws<BadRequestException>(() =>
            ExternalRepoUrlBuilder.Build(GitProviderType.Gitea, null, "owner", "repo", GitAuthType.HttpsToken));
    }
}
