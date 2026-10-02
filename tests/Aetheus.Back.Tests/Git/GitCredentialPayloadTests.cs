// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests.Git;

public class GitCredentialPayloadTests
{
    [Fact]
    public void TryParse_HttpsToken_RoundTrips()
    {
        const string json = """{"authType":"HttpsToken","username":"x-access-token","token":"ghp_secret"}""";
        var parsed = GitCredentialPayload.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(GitAuthType.HttpsToken, parsed!.AuthType);
        Assert.Equal("x-access-token", parsed.Username);
        Assert.Equal("ghp_secret", parsed.Token);
    }

    [Fact]
    public void TryParse_Ssh_ReadsKeyAndKnownHosts()
    {
        const string json = """{"authType":"Ssh","privateKeyPem":"-----KEY-----","knownHosts":"github.com ssh-ed25519 AAAA"}""";
        var parsed = GitCredentialPayload.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(GitAuthType.Ssh, parsed!.AuthType);
        Assert.Equal("-----KEY-----", parsed.PrivateKeyPem);
        Assert.Equal("github.com ssh-ed25519 AAAA", parsed.KnownHosts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void TryParse_EmptyOrMalformed_ReturnsNull(string? input)
    {
        Assert.Null(GitCredentialPayload.TryParse(input));
    }
}
