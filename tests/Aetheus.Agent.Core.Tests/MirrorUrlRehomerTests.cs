// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="MirrorUrlRehomer"/> - the agent-side clone-URL re-homing that makes the
/// pipeline checkout reach the backend from wherever THIS agent is (VPS-sim / SSH tunnel / direct) with
/// no server-side CloneBaseUrl config. Verifies internal mirror URLs get their authority swapped to the
/// agent base while external repos and malformed inputs pass through untouched.
/// </summary>
public sealed class MirrorUrlRehomerTests
{
    [Theory]
    // Tunnelled remote VPS: backend frozen on host.docker.internal, agent reaches it at localhost.
    [InlineData("http://host.docker.internal:5300/git/1/aetheusgit.git", "http://localhost:5300",
                "http://localhost:5300/git/1/aetheusgit.git")]
    // VPS-sim: agent base IS host.docker.internal - stays correct.
    [InlineData("http://localhost:5300/git/2/repo.git", "http://host.docker.internal:5300",
                "http://host.docker.internal:5300/git/2/repo.git")]
    // Scheme + port both swapped to the agent base.
    [InlineData("https://frozen.example:5301/git/7/svc.git", "http://localhost:5300",
                "http://localhost:5300/git/7/svc.git")]
    public void RehomeToAgentBase_MirrorUrl_SwapsAuthorityKeepsPath(string url, string agentBase, string expected)
        => Assert.Equal(expected, MirrorUrlRehomer.RehomeToAgentBase(url, agentBase));

    [Fact]
    public void RehomeToAgentBase_PreservesEmbeddedCredentials()
    {
        var result = MirrorUrlRehomer.RehomeToAgentBase(
            "http://user:pass@host.docker.internal:5300/git/1/aetheusgit.git", "http://localhost:5300");
        Assert.Equal("http://user:pass@localhost:5300/git/1/aetheusgit.git", result);
    }

    [Theory]
    // External repos - not our mirror shape, must pass through untouched.
    [InlineData("https://github.com/owner/repo.git", "http://localhost:5300")]
    [InlineData("https://gitlab.example.com/group/sub/repo.git", "http://localhost:5300")]
    // /git/ present but middle segment is not a positive int -> not a mirror path.
    [InlineData("http://host/git/abc/repo.git", "http://localhost:5300")]
    [InlineData("http://host/git/0/repo.git", "http://localhost:5300")]
    // Path does not end in .git.
    [InlineData("http://host/git/1/repo", "http://localhost:5300")]
    // Malformed / empty inputs.
    [InlineData("not-a-url", "http://localhost:5300")]
    [InlineData("http://host/git/1/repo.git", "")]
    [InlineData("", "http://localhost:5300")]
    public void RehomeToAgentBase_NonMirrorOrInvalid_ReturnsUnchanged(string url, string agentBase)
        => Assert.Equal(url, MirrorUrlRehomer.RehomeToAgentBase(url, agentBase));

    [Fact]
    public void RehomeToAgentBase_DefaultPortAgentBase_OmitsPort()
    {
        var result = MirrorUrlRehomer.RehomeToAgentBase(
            "http://host.docker.internal:5300/git/1/aetheusgit.git", "https://aetheus-api.example.com");
        Assert.Equal("https://aetheus-api.example.com/git/1/aetheusgit.git", result);
    }
}
