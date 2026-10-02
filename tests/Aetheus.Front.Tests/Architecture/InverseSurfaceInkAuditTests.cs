// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Recette R-411 (and R-400 before it): OmniEurope.Blazor's inverse surface is a mid grey (#555555 in
/// light mode, #4b4b4b in dark mode), not a dark console background. Aetheus had painted panels, hovers
/// and code blocks with it while keeping the page's ink, which is dark in light mode: the text vanished,
/// and each new view repeated the mistake. The only ink OE guarantees on that surface is
/// <c>--omni-color-on-inverse</c>, so every app.css rule that paints its background with the inverse
/// surface must set that ink in the same rule. Anything else belongs on a page surface
/// (<c>--omni-color-surface</c>, <c>-surface-muted</c>, <c>-surface-hover</c>, <c>-surface-highlight</c>).
/// </summary>
public class InverseSurfaceInkAuditTests
{
    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Rule = new(
        @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex InverseBackground = new(
        @"(?<![-\w])background(?:-color)?\s*:[^;]*--omni-color-inverse-surface",
        RegexOptions.CultureInvariant);

    private static readonly Regex OnInverseInk = new(
        @"(?<![-\w])color\s*:[^;]*--omni-color-on-inverse",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryInverseSurfaceBackground_CarriesTheOnInverseInk()
    {
        var css = Comment.Replace(
            File.ReadAllText(Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css")),
            string.Empty);

        var rules = Rule.Matches(css).ToList();
        var painted = rules.Where(rule => InverseBackground.IsMatch(rule.Groups["body"].Value)).ToList();
        var violations = painted
            .Where(rule => !OnInverseInk.IsMatch(rule.Groups["body"].Value))
            .Select(rule => rule.Groups["selector"].Value.Trim())
            .ToList();

        // The scan must see the console blocks that do use the surface correctly, or it proves nothing.
        Assert.Contains(painted, rule => rule.Groups["selector"].Value.Trim() == ".docker-compose-editor");
        Assert.True(
            violations.Count == 0,
            "These rules paint OE's inverse surface (a mid grey) without its on-inverse ink, so their text "
            + "is dark on grey in light mode. Set color: var(--omni-color-on-inverse) in the rule, or use a "
            + "page surface token instead:\n  " + string.Join("\n  ", violations));
    }
}
