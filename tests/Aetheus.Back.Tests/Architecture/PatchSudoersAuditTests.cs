// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// ADR-024 4.1: the argv-exact <c>AETHEUS_PATCH</c> sudoers drop-in written by
/// <c>deploy/scripts/install-agent-linux.sh</c> must match the exact argv the agent sends via
/// <c>sudo -n ...</c> (<see cref="SystemPackageUpgradeExecutor.BuildAptUpgradeArgv"/>). A drift in the
/// binary path, verb, or the <c>-y</c> flag would make sudo reject the call at runtime (capability dead
/// on arrival). Modeled on <see cref="ManageablePackagesSudoersAuditTests"/>.
/// </summary>
public class PatchSudoersAuditTests
{
    [Fact]
    public void PatchDropIn_ArgvForm_MatchesUpgradeExecutor()
    {
        // R-249: the drop-in is the patch/sudoers.d/aetheus-patch template the installer renders.
        Assert.Contains(
            "render_host_config patch/sudoers.d/aetheus-patch \"$PATCH_MANAGE_SUDOERS_FILE\"",
            LinuxHostConfigTemplates.Installer(),
            StringComparison.Ordinal);
        var script = LinuxHostConfigTemplates.Read("patch/sudoers.d/aetheus-patch");

        var blockStart = script.IndexOf("Cmnd_Alias AETHEUS_PATCH", StringComparison.Ordinal);
        Assert.True(blockStart >= 0, "Cmnd_Alias AETHEUS_PATCH not found in patch/sudoers.d/aetheus-patch");
        var blockEnd = script.IndexOf("Defaults!AETHEUS_PATCH", blockStart, StringComparison.Ordinal);
        Assert.True(blockEnd > blockStart, "Defaults!AETHEUS_PATCH terminator not found");
        var block = script[blockStart..blockEnd];

        // Drop the leading `-n` (a sudo flag, not part of the sudoers Cmnd match).
        var expected = string.Join(' ', SystemPackageUpgradeExecutor.BuildAptUpgradeArgv().Where(a => a != "-n"));
        Assert.Contains(expected, block, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchDropIn_IsGatedByItsOwnFlag()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));
        // The drop-in must be opt-in (its own flag) and written only when enabled.
        Assert.Contains("--enable-patch-manage", script, StringComparison.Ordinal);
        Assert.Contains("write_patch_manage_sudoers", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
