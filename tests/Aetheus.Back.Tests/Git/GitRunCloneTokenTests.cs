// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.Git;

public class GitRunCloneTokenTests
{
    private static IConfiguration Config(string key = "unit-test-encryption-key-32chars!") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:EncryptionKey"] = key })
            .Build();

    private static readonly DateTime Now = new(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Mint_then_validate_roundtrips_for_same_run_and_project()
    {
        var config = Config();
        var (user, pass) = GitRunCloneToken.Mint(config, runId: 39, projectId: 1, Now, GitRunCloneToken.DefaultTtl);

        Assert.StartsWith(GitRunCloneToken.UsernamePrefix, user);
        Assert.Equal(39, GitRunCloneToken.Validate(config, user, pass, projectId: 1, Now));
    }

    [Fact]
    public void Validate_rejects_a_different_project()
    {
        var config = Config();
        var (user, pass) = GitRunCloneToken.Mint(config, runId: 39, projectId: 1, Now, GitRunCloneToken.DefaultTtl);

        Assert.Null(GitRunCloneToken.Validate(config, user, pass, projectId: 2, Now));
    }

    [Fact]
    public void Validate_rejects_an_expired_token()
    {
        var config = Config();
        var (user, pass) = GitRunCloneToken.Mint(config, runId: 39, projectId: 1, Now, TimeSpan.FromHours(1));

        Assert.Null(GitRunCloneToken.Validate(config, user, pass, projectId: 1, Now.AddHours(2)));
    }

    [Fact]
    public void Validate_rejects_a_token_signed_with_a_different_key()
    {
        var (user, pass) = GitRunCloneToken.Mint(Config("key-a-32-chars-padding-padding!!"), 39, 1, Now, GitRunCloneToken.DefaultTtl);

        Assert.Null(GitRunCloneToken.Validate(Config("key-b-32-chars-padding-padding!!"), user, pass, 1, Now));
    }

    [Fact]
    public void Validate_rejects_a_tampered_payload()
    {
        var config = Config();
        var (user, pass) = GitRunCloneToken.Mint(config, 39, 1, Now, GitRunCloneToken.DefaultTtl);
        var tampered = pass[..^2] + (pass[^1] == 'a' ? "bb" : "aa");

        Assert.Null(GitRunCloneToken.Validate(config, user, tampered, 1, Now));
    }

    [Fact]
    public void Validate_rejects_a_non_run_username()
    {
        var config = Config();
        Assert.Null(GitRunCloneToken.Validate(config, "alice", "whatever", 1, Now));
    }

    [Theory]
    [InlineData("http://localhost:5300/git/1/toto.git", "http://host.docker.internal:5300/git/1/toto.git")]
    [InlineData("https://old-domain.example/git/42/app.git", "http://host.docker.internal:5300/git/42/app.git")]
    public void Rehome_swaps_only_the_authority_of_a_mirror_url(string stored, string expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GitLight:CloneBaseUrl"] = "http://host.docker.internal:5300" })
            .Build();

        Assert.Equal(expected, MirrorCloneUrl.Rehome(config, stored));
    }

    [Theory]
    [InlineData("https://github.com/acme/app.git")]
    // Same /git/x/y.git shape but a NON-numeric middle segment - not one of our mirror paths (the
    // projectId is always an int), so it must be left untouched rather than re-homed.
    [InlineData("https://github.com/git/acme/app.git")]
    [InlineData("https://example.test/git/0/app.git")]
    public void Rehome_leaves_a_non_mirror_url_unchanged(string stored)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GitLight:CloneBaseUrl"] = "http://host.docker.internal:5300" })
            .Build();

        Assert.Equal(stored, MirrorCloneUrl.Rehome(config, stored));
    }
}
