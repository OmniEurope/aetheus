// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// ADR-024 4.2: the <c>aetheus-firewall</c> sudoers drop-in written by
/// <c>deploy/scripts/install-agent-linux.sh</c> must grant exactly the root-owned helper path the agent
/// invokes (<see cref="FirewallOperationExecutor.HelperPath"/>). A drift means the capability is dead on
/// arrival. Also pins the opt-in flag + helper writer + anti-lockout logic being present.
/// </summary>
public class FirewallSudoersAuditTests
{
    private static string Script() => File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));

    [Fact]
    public void FirewallDropIn_GrantsExactlyTheHelperPath()
    {
        var script = Script();
        // R-249: the drop-in is the firewall/sudoers.d/aetheus-firewall template the installer renders.
        Assert.Contains(
            "render_host_config firewall/sudoers.d/aetheus-firewall \"$FIREWALL_MANAGE_SUDOERS_FILE\"",
            script,
            StringComparison.Ordinal);
        var dropIn = LinuxHostConfigTemplates.Read("firewall/sudoers.d/aetheus-firewall");
        var start = dropIn.IndexOf("Cmnd_Alias AETHEUS_FIREWALL", StringComparison.Ordinal);
        Assert.True(start >= 0, "Cmnd_Alias AETHEUS_FIREWALL not found in firewall/sudoers.d/aetheus-firewall");
        var end = dropIn.IndexOf("Defaults!AETHEUS_FIREWALL", start, StringComparison.Ordinal);
        Assert.True(end > start, "Defaults!AETHEUS_FIREWALL terminator not found");
        var block = dropIn[start..end];

        // The grant references the same helper path the agent execs (via the FIREWALL_HELPER_PATH placeholder).
        Assert.Contains("#{FIREWALL_HELPER_PATH}#", block, StringComparison.Ordinal);
        Assert.Contains("FIREWALL_HELPER_PATH=\"" + FirewallOperationExecutor.HelperPath + "\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Firewall_IsOptIn_WithHelperAndAntiLockout()
    {
        var script = Script();
        Assert.Contains("--enable-firewall-manage", script, StringComparison.Ordinal);
        Assert.Contains("write_firewall_manage", script, StringComparison.Ordinal);
        Assert.Contains("render_host_config firewall/aetheus-firewall \"$FIREWALL_HELPER_PATH\"", script, StringComparison.Ordinal);
        // Anti-lockout logic must live in the helper: never close the admin port; auto-allow before enable.
        Assert.Contains(
            "would close the administration port",
            LinuxHostConfigTemplates.Read("firewall/aetheus-firewall"),
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
