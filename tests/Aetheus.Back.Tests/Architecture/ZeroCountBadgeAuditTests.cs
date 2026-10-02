// SPDX-License-Identifier: EUPL-1.2

using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-003 lot 7: a counter badge reading "0" says nothing the empty list beside it does not already
/// say, and it costs a line of room on every row. <c>CountBadge</c> makes the rule structural, so this
/// refuses a raw <c>OmniBadge</c> whose text is a plain count - the shape that keeps coming back.
/// </summary>
public sealed class ZeroCountBadgeAuditTests
{
    [Fact]
    public void A_Plain_Count_Uses_CountBadge()
    {
        var offenders = FrontRazorFiles()
            .SelectMany(file => Regex
                .Matches(file.Text, @"<OmniBadge[^>]*Text=""@[\w.()]*?(Count|_totalCount|Length)(\(\))?\.ToString\(\)""")
                .Select(_ => file.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These render a raw counter badge that would show \"0\"; use <CountBadge>: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_Tile_Moves_Under_The_Pointer()
    {
        // PLAN-003 lots 8, 10 and 26: a card that lifts on hover shifts the text the reader is aiming
        // at. The tint and the border already say "clickable".
        var css = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        var hoverBlocks = Regex.Matches(css, @":hover[^{]*\{[^}]*\}", RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(block => block.Contains("translateY(-", StringComparison.Ordinal))
            .ToList();

        Assert.True(hoverBlocks.Count == 0,
            $"{hoverBlocks.Count} hover rule(s) still lift their element: {string.Join(" | ", hoverBlocks)}");
    }

    private static IEnumerable<(string Name, string Text)> FrontRazorFiles() =>
        RepositoryScan.Enumerate(Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front"), "*.razor")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)));
}
