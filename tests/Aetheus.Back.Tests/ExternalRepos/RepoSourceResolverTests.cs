// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.Tests.ExternalRepos;

public class RepoSourceResolverTests
{
    private readonly IProjectRepository _projectRepo = Substitute.For<IProjectRepository>();
    private readonly IGitLightRepository _lightRepo = Substitute.For<IGitLightRepository>();

    private RepoSourceResolver Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aetheus:PublicApiBaseUrl"] = "https://aetheus.example.com"
            })
            .Build();
        // HttpContext is null here, so resolution falls to the configured PublicApiBaseUrl (config-first).
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        return new RepoSourceResolver(_projectRepo, _lightRepo, config, httpContextAccessor);
    }

    [Fact]
    public async Task Internal_ReturnsRepositoryUrl_NotExternal()
    {
        _projectRepo.FindProjectAsync(7, Arg.Any<CancellationToken>())
            .Returns(new Project { Id = 7, RepositoryUrl = "https://github.com/acme/app.git", DefaultBranch = "main" });

        var source = await Build().ResolveAsync(7, ct: TestContext.Current.CancellationToken);

        Assert.False(source.IsExternal);
        Assert.Equal("https://github.com/acme/app.git", source.CloneUrl);
    }

    [Fact]
    public async Task External_ReturnsMirrorSmartHttpUrl()
    {
        _projectRepo.FindProjectAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Project { Id = 9, GitConnectionId = 3, RepositoryUrl = "stale", DefaultBranch = "develop" });
        _lightRepo.GetByProjectAsync(9, Arg.Any<CancellationToken>())
            .Returns([new GitInternalRepo { ProjectId = 9, Slug = "app", GitConnectionId = 3 }]);

        var source = await Build().ResolveAsync(9, ct: TestContext.Current.CancellationToken);

        Assert.True(source.IsExternal);
        Assert.True(source.IsMirrorBacked);
        Assert.Equal("https://aetheus.example.com/git/9/app.git", source.CloneUrl);
    }
}
