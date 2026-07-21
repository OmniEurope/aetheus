// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-006 4.2: the anti-lockout guard must refuse any operation that would close the administration
/// (SSH) port. Pure logic shared by the backend and the agent.
/// </summary>
public class FirewallLockoutGuardTests
{
    [Theory]
    [InlineData(OperationKind.FirewallDeny, 22, true)]
    [InlineData(OperationKind.FirewallDeleteRule, 22, true)]
    [InlineData(OperationKind.FirewallAllow, 22, false)]     // allowing the admin port is fine
    [InlineData(OperationKind.FirewallDeny, 8080, false)]    // a non-admin port can be closed
    [InlineData(OperationKind.FirewallDeleteRule, 443, false)]
    public void IsLockoutRisk_DefaultAdminPort(OperationKind op, int port, bool expected)
        => Assert.Equal(expected, FirewallLockoutGuard.IsLockoutRisk(op, port));

    [Fact]
    public void IsLockoutRisk_HonorsCustomAdminPorts()
    {
        var admin = new HashSet<int> { 22, 2222 };

        Assert.True(FirewallLockoutGuard.IsLockoutRisk(OperationKind.FirewallDeny, 2222, admin));
        Assert.False(FirewallLockoutGuard.IsLockoutRisk(OperationKind.FirewallDeny, 22, new HashSet<int> { 2222 }));
    }

    [Fact]
    public void IsLockoutRisk_NonFirewallOp_False()
        => Assert.False(FirewallLockoutGuard.IsLockoutRisk(OperationKind.FirewallAllow, 22));
}
