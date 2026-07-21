// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-006 4.1: the argv-exact <c>AETHEUS_PATCH</c> sudoers drop-in written by
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
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));

        var blockStart = script.IndexOf("Cmnd_Alias AETHEUS_PATCH", StringComparison.Ordinal);
        Assert.True(blockStart >= 0, "Cmnd_Alias AETHEUS_PATCH not found in install-agent-linux.sh");
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PatchSudoersAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
