// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// ButtonStyle.Secondary and BadgeStyle.Secondary are banned: Secondary rendered nearly identically
/// to Light and had split 117/224 across the app for no expressible reason (decision of 2026-08-19).
/// Its whole icon family moved to Light in ButtonFamilyAndGridNavigationAuditTests.
///
/// Scope note: this guard checks ONLY that ban. It deliberately does not police Success/Info/Warning
/// on buttons the way the Generic and Atlas contracts do, because Aetheus colours a button by its
/// ICON FAMILY - one icon, one colour, across ~100 pages - and that mapping is enforced by
/// ButtonFamilyAndGridNavigationAuditTests. The two rules are incompatible, and Aetheus chose the
/// family one; see claude-ui-patterns.md.
/// </summary>
public class ButtonSemanticsAuditTests
{

    [Fact]
    public void NoButton_UsesSecondaryStyle()
    {
        var offenders = FrontFiles()
            .Select(f => (Rel: Rel(f), Count: Regex.Matches(File.ReadAllText(f), @"ButtonStyle\.Secondary").Count))
            .Where(x => x.Count > 0)
            .ToList();

        Assert.True(offenders.Count == 0,
            "ButtonStyle.Secondary is banned - use ButtonStyle.Light (claude-ui-patterns.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => $"  - {o.Rel} ({o.Count})")));
    }

    [Fact]
    public void NoBadge_UsesSecondaryStyle()
    {
        var offenders = FrontFiles()
            .Select(f => (Rel: Rel(f), Count: Regex.Matches(File.ReadAllText(f), @"BadgeStyle\.Secondary").Count))
            .Where(x => x.Count > 0)
            .ToList();

        Assert.True(offenders.Count == 0,
            "BadgeStyle.Secondary is banned - use BadgeStyle.Light (claude-ui-patterns.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => $"  - {o.Rel} ({o.Count})")));
    }


    private static IEnumerable<string> FrontFiles()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        return RepositoryScan.Enumerate(root, "*.*")
            .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string Rel(string fullPath)
    {
        var root = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}