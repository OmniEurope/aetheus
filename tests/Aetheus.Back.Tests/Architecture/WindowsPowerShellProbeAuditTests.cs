// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public class WindowsPowerShellProbeAuditTests
{
    [Fact]
    public void ProbeIsBoundedKilledAndObservable()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Aetheus.Agent.Windows",
            "Executors",
            "WindowsShellExecutor.cs"));

        Assert.Contains("TimeSpan.FromSeconds(3)", source, StringComparison.Ordinal);
        Assert.Contains("WaitForExit((int)ProbeTimeout.TotalMilliseconds)", source, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("exceeded {TimeoutSeconds}s", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch {", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
