// SPDX-License-Identifier: EUPL-1.2

using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-003 lot 3 / D8: outside a table every control stands at one height, the measured
/// <c>--aetheus-control-height</c> (1.75rem, read in the browser on the reference button, not
/// chosen). Before this, the same screen showed buttons at 20, 28, 32 and 50px, badges at 20 and
/// 20.8, header icons at 32 and input fields at 40: twelve pixels taller than the button beside them.
///
/// Two ways that comes back, so two guards: a per-call-site <c>ButtonSize</c> that reintroduces a
/// second height in markup, and a literal height written on a control selector in the stylesheet.
/// A table is excluded on purpose: a row action is sized by the grid row (lot 4).
/// </summary>
public sealed class ControlHeightAuditTests
{
    private const string Token = "var(--aetheus-control-height)";

    /// <summary>
    /// Selectors that name a control rather than a layout box. A block whose selector list touches
    /// one of these may not carry a literal height.
    /// </summary>
    private static readonly string[] ControlSelectors =
    [
        ".omni-button", ".omni-badge", ".omni-text-box", ".omni-input", ".omni-combo", ".header-action-btn",
    ];

    /// <summary>
    /// Written exceptions, each with its reason. Anything else must use the token.
    /// </summary>
    private static readonly string[] ExemptSelectors =
    [
        // A row action is governed by the grid row height, not by the page control token.
        ".omni-data-grid", ".aetheus-grid",
        // The grade letter follows the text line it annotates (lots 7 and 18).
        ".grade-badge",
    ];

    [Fact]
    public void No_Call_Site_Sets_Its_Own_Button_Size()
    {
        var files = FrontRazorFiles();
        Assert.True(files.Count >= 100, $"Only {files.Count} razor files scanned; the guard is not seeing the app.");

        var offenders = files
            .Where(file => file.Text.Contains("Size=\"ButtonSize.", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A per-call-site ButtonSize is a second control height. Remove it and let "
            + $"{Token} decide: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_Control_Selector_Carries_A_Literal_Height()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        var blocks = Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline)
            .Select(match => (Selector: match.Groups["selector"].Value, Body: match.Groups["body"].Value))
            .Where(block => ControlSelectors.Any(selector =>
                Regex.IsMatch(block.Selector, Regex.Escape(selector) + @"(?![\w-])")))
            .Where(block => !ExemptSelectors.Any(exempt =>
                block.Selector.Contains(exempt, StringComparison.Ordinal)))
            .ToList();

        Assert.True(blocks.Count >= 5, $"Only {blocks.Count} control blocks found; the scan is not reading app.css.");

        var offenders = blocks
            .Where(block => Regex.IsMatch(block.Body, @"(?<!-)\b(?:min-|max-)?height:\s*[0-9.]+(?:rem|px)"))
            .Select(block => block.Selector.Trim().ReplaceLineEndings(" "))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These write a control height instead of using {Token}: {string.Join(" | ", offenders)}");
    }

    /// <summary>
    /// The maintained OE controls use the shared token as their height rather than inheriting the
    /// package's larger default geometry.
    /// </summary>
    [Fact]
    public void Omni_Controls_Use_The_Shared_Height()
    {
        var css = Regex.Replace(File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        string Body(string selector) =>
            string.Join("\n", Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline)
                .Where(match => match.Groups["selector"].Value.Split(',').Any(part => part.Trim() == selector))
                .Select(match => match.Groups["body"].Value));

        foreach (var selector in new[] { ".omni-button", ".omni-text-box", ".omni-input", ".omni-combo" })
        {
            Assert.Contains($"min-height: {Token};", Body(selector), StringComparison.Ordinal);
            Assert.Contains($"height: {Token};", Body(selector), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// PLAN-003 lot 1 / D3: the page header is a height, not a floor, and does not shrink, so the
    /// content starts at the same Y everywhere; its title line has one fixed height. Since recette R-395
    /// the header is OE's OmniPageHeader, whose frame and title line carry that fixed geometry: app.css may
    /// size the title's text (recette R-014, 1.875rem) but never the height of the frame, of the row or of
    /// the title line, except to let a header the reader opened on its toggle grow (the fold, R-324).
    /// </summary>
    [Fact]
    public void The_Page_Header_Frame_Has_One_Height()
    {
        var css = Regex.Replace(File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var rules = Regex.Matches(css, @"(?<selector>[^{}]*omni-page-header__(?:frame|title|row)[^{}]*)\{(?<body>[^}]*)\}").ToList();
        Assert.NotEmpty(rules);

        var offenders = rules
            .Where(rule => Regex.IsMatch(rule.Groups["body"].Value,
                @"(?<![-\w])(height|block-size|min-height|max-height|min-block-size|max-block-size|line-height)\s*:"))
            .Where(rule => !rule.Groups["selector"].Value.Contains("omni-page-header__details--open", StringComparison.Ordinal))
            .Select(rule => rule.Groups["selector"].Value.Trim())
            .ToList();

        Assert.True(offenders.Count == 0,
            "These app.css rules change the page header's fixed geometry: " + string.Join(", ", offenders));
    }

    private static List<(string Name, string Text)> FrontRazorFiles() =>
        RepositoryScan.Enumerate(Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front"), "*.razor")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .ToList();
}
