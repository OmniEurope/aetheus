// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.ExternalRepos;

/// <summary>
/// The clone base can be set per environment (Compose passes <c>GitLight__CloneBaseUrl</c>, empty where
/// an environment does not set it): production shows clone URLs on the app's name, served by the app
/// vhost's /git/ proxy, while every other environment keeps the API's.
/// </summary>
public sealed class MirrorCloneUrlTests
{
    private static IConfiguration Config(string? cloneBase, string? publicApi = "https://api.example.test") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitLight:CloneBaseUrl"] = cloneBase,
            ["Aetheus:PublicApiBaseUrl"] = publicApi
        }).Build();

    [Fact]
    public void AConfiguredCloneBase_NamesTheCloneUrl()
    {
        Assert.Equal("https://app.example.test/git/1/aetheus.git",
            MirrorCloneUrl.Build(Config("https://app.example.test/"), null, 1, "aetheus"));
        Assert.Equal("https://app.example.test/git/1/aetheus.git",
            MirrorCloneUrl.Rehome(Config("https://app.example.test"), "https://old-api.example.test/git/1/aetheus.git"));
    }

    /// <summary>An empty value is what Compose hands an environment that sets nothing: it must fall back
    /// to the public API, not leave a stored URL on a host that may no longer exist.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AnEmptyCloneBase_FallsBackToThePublicApi(string? cloneBase)
    {
        Assert.Equal("https://api.example.test/git/1/aetheus.git",
            MirrorCloneUrl.Rehome(Config(cloneBase), "https://old-api.example.test/git/1/aetheus.git"));
        Assert.Equal("https://api.example.test/git/1/aetheus.git",
            MirrorCloneUrl.Build(Config(cloneBase), null, 1, "aetheus"));
    }

    [Theory]
    [InlineData("https://app.example.test/git/1/aetheus.git", true)]
    [InlineData("https://github.com/OmniEurope/OmniEurope.Blazor.git", false)]
    [InlineData("https://other.example.test/git/1/aetheus.git", false)]
    [InlineData("https://app.example.test/other/1/aetheus.git", false)]
    public void OnlyTheConfiguredInternalCloneReceivesARunToken(string cloneUrl, bool expected)
    {
        Assert.Equal(expected, MirrorCloneUrl.IsCurrentInternalClone(
            Config("https://app.example.test"), cloneUrl));
    }
}
