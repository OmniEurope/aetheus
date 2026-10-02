// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests;

public sealed class PortsentryValidationTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("22,80,443,65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("22,70000", false)]
    [InlineData("-1", false)]
    [InlineData("22,", false)]
    public void IsValidPortList_EnforcesTcpUdpRange(string ports, bool expected)
    {
        Assert.Equal(expected, PortsentryValidation.IsValidPortList(ports));
    }
}
