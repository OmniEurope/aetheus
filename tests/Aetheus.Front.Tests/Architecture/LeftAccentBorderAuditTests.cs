// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Keeps decorative colored strips off the left edge of cards, tiles, navigation items and rows.
/// Neutral timeline and hierarchy guides remain explicitly allow-listed because they communicate
/// structure rather than status or decoration.
/// </summary>
public class LeftAccentBorderAuditTests
{
    private static readonly HashSet<string> AllowedStructuralBorders =
    [
        "0",
        "1px solid var(--rz-base-700, #2e2e2e)",
        "2px dashed var(--rz-base-500)",
        "1px solid var(--rz-base-600, #444)",
        "0.125rem solid var(--rz-base-600, #333)"
    ];

    [Fact]
    public void AppCss_DoesNotUseDecorativeLeftAccentBorders()
    {
        var cssPath = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css");
        var violations = new List<string>();
        var lines = File.ReadAllLines(cssPath);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            foreach (Match match in Regex.Matches(
                line,
                @"border-(?:left|inline-start)\s*:\s*(?<value>[^;]+);",
                RegexOptions.IgnoreCase))
            {
                var value = match.Groups["value"].Value.Trim();
                if (!AllowedStructuralBorders.Contains(value))
                    violations.Add($"app.css:{index + 1}: {match.Value.Trim()}");
            }

            if (line.Contains("box-shadow", StringComparison.OrdinalIgnoreCase)
                && line.Contains("inset", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"app.css:{index + 1}: ombre interne utilisée comme accent latéral");
            }
        }

        Assert.True(violations.Count == 0,
            "Les bordures d'accent décoratives à gauche sont interdites. Utiliser une icône, un badge, "
            + "une couleur de texte ou un fond pour communiquer l'état :\n  "
            + string.Join("\n  ", violations));
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
