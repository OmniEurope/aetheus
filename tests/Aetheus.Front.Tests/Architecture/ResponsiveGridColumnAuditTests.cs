// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards the product-wide grid contract: viewport width may introduce horizontal
/// scrolling, but must never hide or collapse a Radzen column.</summary>
public sealed class ResponsiveGridColumnAuditTests
{
    [Fact]
    public void GridColumns_AreNeverHiddenAtResponsiveBreakpoints()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(frontDir, "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"\bcol-hide-(?:xl|lg|md|mobile)\b"))
            {
                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add($"{Path.GetRelativePath(frontDir, file)}:{line}");
            }
        }

        Assert.True(violations.Count == 0,
            "Grid columns must remain visible and use horizontal scrolling instead of col-hide-* classes:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryGrid_HasAHorizontalOverflowAndDefaultWidthContract()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".rz-data-grid .rz-data-grid-data {\n    overflow-x: auto;", css);
        Assert.Contains(
            ".rz-data-grid .rz-grid-table > colgroup > col:not([style^=\"width:\"]):not([style*=\";width:\"]):not([style*=\"; width:\"]) {\n    width: 10rem;",
            css);
        Assert.DoesNotContain(".col-hide-", css, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ResponsiveGridColumnAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
