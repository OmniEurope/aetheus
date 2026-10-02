// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Recette R-007 (PLAN-008 lot 45): Aetheus uses the breakpoints of OmniEurope and no others. OE
/// sizes its columns at 40rem (sm), 64rem (md) and 80rem (lg); a width query in Aetheus is one of
/// those thresholds, written as min-width N or max-width N-0.01rem. Before this rule the stylesheet
/// carried fifteen different thresholds (576px, 768px, 767.98px, 48rem, 37.5rem, 992px...).
/// </summary>
public class BreakpointAuditTests
{
    private static readonly HashSet<string> Allowed =
    [
        "min-width: 40rem", "max-width: 39.99rem",
        "min-width: 64rem", "max-width: 63.99rem",
        "min-width: 80rem", "max-width: 79.99rem"
    ];

    [Theory]
    [InlineData("src/Aetheus.Front/wwwroot/css/app.css")]
    [InlineData("src/Aetheus.Front/wwwroot/js/layout.js")]
    public void WidthQueries_UseOnlyTheOmniEuropeBreakpoints(string relativePath)
    {
        var path = Path.Combine(RepositoryScan.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var lines = File.ReadAllLines(path);
        var violations = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            // Only media conditions: a min-width or max-width property is a size, not a breakpoint.
            if (!lines[index].Contains("@media", StringComparison.Ordinal)
                && !lines[index].Contains("matchMedia(", StringComparison.Ordinal))
                continue;
            foreach (Match match in Regex.Matches(lines[index], @"(?:min|max)-width\s*:\s*[0-9.]+(?:px|rem|em)"))
            {
                var query = Regex.Replace(match.Value, @"\s*:\s*", ": ");
                if (!Allowed.Contains(query))
                    violations.Add($"{relativePath}:{index + 1}: {match.Value}");
            }
        }

        Assert.True(violations.Count == 0,
            "Width queries outside the OmniEurope breakpoints (40, 64, 80rem):" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
