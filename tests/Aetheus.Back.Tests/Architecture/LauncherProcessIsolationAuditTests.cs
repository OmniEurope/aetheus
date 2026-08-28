// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// The launcher terminates whatever holds the development ports so a stale stack cannot block a new
/// run. That is only safe while "whatever holds the port" is one of THIS checkout's own processes.
///
/// It was not. The command-line sweep carried a worktree boundary and said so in its comment; the
/// port pass right above it called Stop-Process -Force on the port's owner with no boundary at all,
/// so a worktree launched on the default ports terminated the parent checkout's backend. The victim
/// sees a bare exit code -1, no exception and no Windows event; the killer logs nothing either. A
/// debugging session lost hours to it before a peer volunteered what it had been doing.
///
/// These guards read the script rather than run it: the behaviour lives in PowerShell that the test
/// host cannot execute, but the invariant that matters is structural. Both passes must consult the
/// same ownership check, and the port pass must consult it before it kills anything.
/// </summary>
public sealed class LauncherProcessIsolationAuditTests
{
    private static string LauncherSource =>
        File.ReadAllText(Path.Combine(RepositoryScan.Root, "scripts", "launch-core.ps1"));

    [Fact]
    public void PortPass_ChecksOwnershipBeforeTerminatingTheOwner()
    {
        // Comments in this region legitimately name Stop-Process while explaining the defect, so the
        // ordering has to be read from executable lines only.
        var portLoop = StripComments(ExtractPortPass(LauncherSource));

        Assert.Contains("Test-AetheusProcessBelongsToCheckout", portLoop, StringComparison.Ordinal);

        var ownershipCheck = portLoop.IndexOf("Test-AetheusProcessBelongsToCheckout", StringComparison.Ordinal);
        var kill = portLoop.IndexOf("Stop-Process", StringComparison.Ordinal);
        Assert.True(
            kill > ownershipCheck,
            "The port pass must decide ownership before it terminates the port's owner; a kill that "
            + "precedes the check is the defect this guard exists for.");
    }

    [Fact]
    public void PortPass_RefusesAForeignOwnerInsteadOfKillingIt()
    {
        var portLoop = ExtractPortPass(LauncherSource);

        // Refusing is the point: a port held by another checkout is genuinely taken, so proceeding
        // would fail to bind anyway. Naming the owner turns a silent execution into a diagnosable one.
        Assert.Contains("throw", portLoop, StringComparison.Ordinal);
        Assert.Contains("does not belong to this checkout", portLoop, StringComparison.Ordinal);
    }

    [Fact]
    public void BothKillPasses_ShareOneDefinitionOfThisCheckout()
    {
        var source = LauncherSource;

        // One helper, so the two passes cannot drift apart again. The regression was precisely that
        // one pass carried the boundary and the other did not.
        Assert.Contains("function Get-AetheusOwnProcessPattern", source, StringComparison.Ordinal);
        Assert.Contains("function Test-AetheusProcessBelongsToCheckout", source, StringComparison.Ordinal);

        var uses = CountOccurrences(source, "Test-AetheusProcessBelongsToCheckout");
        Assert.True(
            uses >= 3,
            $"Expected the ownership check to be declared once and used by both kill passes, saw {uses} mentions.");
    }

    [Fact]
    public void OwnershipBoundary_IsTheProjectDirectoryAndNotTheBareProductName()
    {
        var source = LauncherSource;
        var helper = Extract(source, "function Get-AetheusOwnProcessPattern", "function Test-AetheusProcessBelongsToCheckout");

        // "$root\src\" and nothing looser: every worktree path contains the bare product name, and
        // $root alone would let the parent checkout claim the worktrees nested under it.
        Assert.Contains("Join-Path $root \"src\"", helper, StringComparison.Ordinal);
        Assert.Contains("DirectorySeparatorChar", helper, StringComparison.Ordinal);
    }

    /// <summary>Drops whole-line PowerShell comments so a guard reads code, not prose about code.</summary>
    private static string StripComments(string source)
        => string.Join(
            '\n',
            source.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static string ExtractPortPass(string source)
        => Extract(source, "function Stop-AetheusProcesses", "if ($ListenersOnly)");

    private static string Extract(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate '{from}' in launch-core.ps1; the guard is reading the wrong shape.");
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not locate '{to}' after '{from}' in launch-core.ps1.");
        return source[start..end];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
