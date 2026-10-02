// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// ButtonStyle.Secondary and OmniTone.Accent are banned: Secondary rendered nearly identically
/// to Light and had split 117/224 across the app for no expressible reason (decision of 2026-08-19).
/// (These are the retired library's style and badge names; the OE button's neutral grey is
/// OmniButtonVariant.Secondary, which stays allowed.)
///
/// Scope note: this guard checks ONLY that ban. Button colours follow STD-BTN (revised 2026-09-28,
/// colour follows importance: Primary the main action of its zone, Secondary the others, Ghost a minor
/// one, Danger a destructive one, never Success), enforced by ButtonRoleColourGuardTests and
/// ButtonFamilyAndGridNavigationAuditTests; see docs/contracts/ui-patterns.md.
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
            "ButtonStyle.Secondary is banned - use ButtonStyle.Light (docs/contracts/ui-patterns.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => $"  - {o.Rel} ({o.Count})")));
    }

    [Fact]
    public void NoBadge_UsesSecondaryStyle()
    {
        var offenders = FrontFiles()
            .Select(f => (Rel: Rel(f), Count: Regex.Matches(File.ReadAllText(f), @"OmniTone\.Secondary").Count))
            .Where(x => x.Count > 0)
            .ToList();

        Assert.True(offenders.Count == 0,
            "OmniTone.Accent is banned - use OmniTone.Neutral (docs/contracts/ui-patterns.md):"
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
