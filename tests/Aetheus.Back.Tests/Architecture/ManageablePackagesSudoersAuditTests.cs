// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Constants;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// S-FEAT-W8KN: the argv-exact <c>AETHEUS_PACKAGE</c> sudoers drop-in written by
/// <c>deploy/scripts/install-agent-linux.sh</c> is the third copy of the managed-package allow-list
/// (after <see cref="ManageablePackages"/> and the backend service map). It is hand-maintained and was
/// previously ungated - a drift between it and <see cref="ManageablePackages"/> silently means an
/// installable package the agent can't sudo (or a protected package it CAN remove). This test parses
/// the drop-in straight from the install script and asserts:
/// <list type="bullet">
///   <item>the INSTALL entries == <see cref="ManageablePackages.All"/> (resolved apt names),</item>
///   <item>the REMOVE entries == <see cref="ManageablePackages.Removable"/> (protected packages excluded),</item>
///   <item><c>apt-get update</c> is allow-listed (fresh-box index refresh before install).</item>
/// </list>
/// </summary>
public class ManageablePackagesSudoersAuditTests
{
    [Fact]
    public void SudoersDropIn_MatchesManageablePackages_All_And_Removable()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));

        // Isolate the Cmnd_Alias AETHEUS_PACKAGE block (up to the Defaults!AETHEUS_PACKAGE lines).
        var blockStart = script.IndexOf("Cmnd_Alias AETHEUS_PACKAGE", StringComparison.Ordinal);
        Assert.True(blockStart >= 0, "Cmnd_Alias AETHEUS_PACKAGE not found in install-agent-linux.sh");
        var blockEnd = script.IndexOf("Defaults!AETHEUS_PACKAGE", blockStart, StringComparison.Ordinal);
        Assert.True(blockEnd > blockStart, "Defaults!AETHEUS_PACKAGE terminator not found");
        var block = script[blockStart..blockEnd];

        var installs = Regex.Matches(block, @"apt-get install -y ([^,\s\\]+)")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removes = Regex.Matches(block, @"apt-get remove -y ([^,\s\\]+)")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            ManageablePackages.All.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            installs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            ManageablePackages.Removable.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            removes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        Assert.Matches(@"apt-get update", block);
    }

    [Fact]
    public void SudoersDropIn_ArgvForm_MatchesPackageOperationExecutor()
    {
        // The previous assertions compared only package NAMES; a drift in the argv SHAPE (absolute
        // binary path, verb position, the `-y` flag) would pass name-equality yet make sudo reject
        // the exact argv the agent sends via `sudo -n ...`. Pin the full command form to the agent's
        // own BuildAptArgv/BuildAptUpdateArgv (dropping the leading `-n`, which is a sudo flag, not
        // part of the sudoers Cmnd match).
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));
        var blockStart = script.IndexOf("Cmnd_Alias AETHEUS_PACKAGE", StringComparison.Ordinal);
        var blockEnd = script.IndexOf("Defaults!AETHEUS_PACKAGE", blockStart, StringComparison.Ordinal);
        var block = script[blockStart..blockEnd];

        static string Cmnd(IReadOnlyList<string> argv) =>
            string.Join(' ', argv.Where(a => a != "-n"));

        // apt-get update
        Assert.Contains(Cmnd(PackageOperationExecutor.BuildAptUpdateArgv()), block, StringComparison.Ordinal);

        // Every installable package's exact install argv must appear verbatim.
        foreach (var pkg in ManageablePackages.All)
            Assert.Contains(Cmnd(PackageOperationExecutor.BuildAptArgv("install", pkg)), block, StringComparison.Ordinal);

        // Every removable package's exact remove argv must appear verbatim.
        foreach (var pkg in ManageablePackages.Removable)
            Assert.Contains(Cmnd(PackageOperationExecutor.BuildAptArgv("remove", pkg)), block, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
