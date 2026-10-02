// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Tests;

public class FirewallValidationTests
{
    [Theory]
    [InlineData("tcp", true)]
    [InlineData("udp", true)]
    [InlineData("TCP", true)]
    [InlineData("icmp", false)]
    [InlineData("", false)]
    public void IsValidProtocol(string proto, bool expected)
        => Assert.Equal(expected, FirewallValidation.IsValidProtocol(proto));

    [Theory]
    [InlineData("any", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("192.168.1.0/24", true)]
    [InlineData("::1", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("not-an-ip", false)]
    [InlineData("10.0.0.0/99", false)]
    [InlineData("", false)]
    public void IsValidSource(string source, bool expected)
        => Assert.Equal(expected, FirewallValidation.IsValidSource(source));
}
