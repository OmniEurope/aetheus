// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Portsentry;

namespace Aetheus.Back.Tests;

public class PortsentryCommandHelperTests
{
    // Mode / port-list validation moved to the shared PortsentryValidation (used by
    // OperationTargetValidator and re-checked agent-side); see OperationTargetValidatorTests and
    // PortsentryOperationExecutorTests. The BuildSetupCommand shell builder was removed with them
    // (typed PortsentrySetup op via the root-owned portsentry-setup helper).

    // --- IsValidIpAddress ---

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("not-an-ip", false)]
    [InlineData("192.168.1.1; rm -rf /", false)]
    public void IsValidIpAddress_ReturnsExpected(string ip, bool expected)
    {
        Assert.Equal(expected, PortsentryCommandHelper.IsValidIpAddress(ip));
    }
}
