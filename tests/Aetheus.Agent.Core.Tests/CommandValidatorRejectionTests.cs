// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class CommandValidatorRejectionTests
{
    private readonly CommandValidator _sut = new(Options.Create(new AetheusAgentOptions()));

    [Fact]
    public void GetRejectionReason_EmptyCommand_ReturnsEmptyReason()
        => Assert.Equal("Empty command", _sut.GetRejectionReason("   "));

    [Fact]
    public void GetRejectionReason_EmptySegmentInChain_Detected()
    {
        var reason = _sut.GetRejectionReason("git status && ");

        Assert.Equal("Empty segment in chain", reason);
    }

    [Fact]
    public void GetRejectionReason_Metacharacter_Blocked()
    {
        var reason = _sut.GetRejectionReason("rm -rf /; echo pwned");

        Assert.NotNull(reason);
        Assert.Contains("metacharacter", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRejectionReason_NotAllowListed_Rejected()
    {
        var reason = _sut.GetRejectionReason("zzzznotarealcommand --do-stuff");

        Assert.NotNull(reason);
        Assert.Contains("allow-list", reason, StringComparison.OrdinalIgnoreCase);
    }
}
