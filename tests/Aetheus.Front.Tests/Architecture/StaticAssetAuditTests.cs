// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

public class StaticAssetAuditTests
{
    [Fact]
    public void UnusedBootstrapDistribution_IsNotPublished()
    {
        var root = FindRepoRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "lib", "bootstrap")));
    }

    [Fact]
    public void RadzenSanitizer_PreservesNativeHeaderSortState()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "js", "a11y-radzen.js"));

        Assert.DoesNotContain("removeAttribute(\"aria-sort\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("querySelectorAll(\n            \"[aria-sort]", source, StringComparison.Ordinal);
        Assert.Contains("table[role='presentation']", source, StringComparison.Ordinal);
        Assert.Contains("presentationTables[i].removeAttribute(\"role\")", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(StaticAssetAuditTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
