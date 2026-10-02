// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.AgentWizard;
namespace Aetheus.Front.Tests.Pages.Servers;

public sealed class AgentInstallFlagsTests
{
    [Fact]
    public void BuildLinux_Deployment_UsesLeastPrivilegeAndCertbotCapability()
    {
        var flags = AgentInstallFlags.BuildLinux(
            includePipelineRunner: true,
            includeServerManagement: false,
            includeDeploymentAgent: true,
            includePatchManagement: false,
            includeFirewallManagement: false,
            includeDocker: false);

        Assert.Contains("--module deployment", flags, StringComparison.Ordinal);
        Assert.Contains("--enable-certbot-manage", flags, StringComparison.Ordinal);
        Assert.DoesNotContain("--module server-management", flags, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLinux_ServerManagement_DoesNotAddStandaloneDockerGrant()
    {
        var flags = AgentInstallFlags.BuildLinux(true, true, false, false, false, true);

        Assert.Contains("--module server-management", flags, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-docker", flags, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-certbot-manage", flags, StringComparison.Ordinal);
    }
}
