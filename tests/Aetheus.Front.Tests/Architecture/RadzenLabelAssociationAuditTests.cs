// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guard against the "unassociated label" anti-pattern on toggle controls, scanning the raw
/// <c>.razor</c> markup (same source-text approach as the other front guards).
///
/// A session proved that Radzen's own external-label mechanisms do NOT click-toggle a
/// <c>RadzenCheckBox</c>/<c>RadzenSwitch</c>: neither a bare adjacent <c>RadzenText</c>, nor
/// <c>RadzenLabel Component="name"</c> (only wires ARIA, not a real clickable association for these
/// two controls), nor a <c>RadzenFormField Text="..."</c> wrapper (the field's label suffers the same
/// gap). The only click-associated pattern for a toggle is the shared <see cref="LabeledToggle"/>
/// component (see its doc comment), which owns the click/keydown handling on a real wrapping
/// <c>&lt;label&gt;</c> element.
///
/// The guard flags a non-decorative <c>RadzenCheckBox</c>/<c>RadzenSwitch</c> whose immediately-
/// adjacent sibling is a <c>RadzenText</c> or a <c>RadzenLabel</c>, or that sits directly inside an
/// enclosing <c>RadzenFormField</c> carrying a <c>Text=</c> attribute. Grid-cell toggles (the column
/// header/title is the label, e.g. <c>RadzenDataGridColumn</c> templates), toggles with no textual
/// label at all (a separate, unaddressed concern - nothing to click-associate), and the decorative
/// <c>aria-hidden</c> control <see cref="LabeledToggle"/> itself renders internally are correctly
/// left untouched: none matches the patterns above.
/// </summary>
public class RadzenLabelAssociationAuditTests
{
    private static readonly string[] ToggleTags = ["<RadzenCheckBox", "<RadzenSwitch"];

    [Fact]
    public void Toggles_DoNotUse_UnassociatedRadzenTextLabel()
    {
        var violations = new List<string>();
        var razorDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(razorDir), $"Front dir not found: {razorDir}");

        foreach (var file in Directory.EnumerateFiles(razorDir, "*.razor", SearchOption.AllDirectories))
        {
            var raw = StripRazorComments(File.ReadAllText(file));
            var rel = Path.GetRelativePath(razorDir, file);

            foreach (var open in ToggleTags)
            {
                var idx = 0;
                while ((idx = raw.IndexOf(open, idx, StringComparison.Ordinal)) >= 0)
                {
                    var end = FindTagEnd(raw, idx);
                    if (end < 0) break;
                    var tag = raw.Substring(idx, end - idx + 1);

                    // Decorative controls carry no interactive label - skip (this is how
                    // LabeledToggle's own internal RadzenCheckBox/RadzenSwitch stays exempt).
                    if (!tag.Contains("aria-hidden", StringComparison.Ordinal))
                    {
                        if (PrecededByRadzenText(raw, idx) || FollowedByRadzenText(raw, end))
                            violations.Add($"{rel}: {open[1..]} sits next to a bare <RadzenText> label - "
                                + "use the <LabeledToggle> component so the label is genuinely clickable");
                        else if (PrecededByRadzenLabel(raw, idx) || FollowedByRadzenLabel(raw, end))
                            violations.Add($"{rel}: {open[1..]} sits next to a <RadzenLabel Component=\"...\"/> - "
                                + "that association does NOT toggle a checkbox/switch; use <LabeledToggle> instead");
                        else if (WrappedByFormFieldWithText(raw, idx))
                            violations.Add($"{rel}: {open[1..]} is wrapped in a <RadzenFormField Text=\"...\"/> - "
                                + "the field's label does NOT toggle a checkbox/switch; use <LabeledToggle> instead");
                    }
                    idx = end + 1;
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Toggle labels must be click-associated via the shared <LabeledToggle> component:\n  "
            + string.Join("\n  ", violations));
    }

    [Theory]
    [InlineData("<RadzenLabel Component=\"enabled\" />\n<RadzenSwitch Name=\"enabled\" />")]
    [InlineData("<RadzenLabel Component=\"enabled\">Enabled</RadzenLabel>\n<RadzenSwitch Name=\"enabled\" />")]
    public void Scanner_Recognizes_SelfClosing_And_Paired_RadzenLabels(string markup)
    {
        var controlStart = markup.IndexOf("<RadzenSwitch", StringComparison.Ordinal);

        Assert.True(PrecededByRadzenLabel(markup, controlStart));
    }

    // The text immediately before the control is a closing </RadzenText> (label rendered before it).
    private static bool PrecededByRadzenText(string raw, int controlStart) =>
        raw.AsSpan(0, controlStart).TrimEnd().EndsWith("</RadzenText>");

    // The very next tag after the control (only whitespace in between) opens a <RadzenText>.
    private static bool FollowedByRadzenText(string raw, int controlEnd)
    {
        var afterStart = controlEnd + 1;
        var nextLt = raw.IndexOf('<', afterStart);
        if (nextLt < 0) return false;
        if (!string.IsNullOrWhiteSpace(raw.Substring(afterStart, nextLt - afterStart))) return false;
        return raw.AsSpan(nextLt).StartsWith("<RadzenText");
    }

    // The text immediately before the control is a self-closing/closing <RadzenLabel .../> or
    // </RadzenLabel> (e.g. the Login.razor RememberMe pattern: checkbox then RadzenLabel Component=).
    private static bool PrecededByRadzenLabel(string raw, int controlStart)
    {
        var before = raw.AsSpan(0, controlStart).TrimEnd();
        if (before.EndsWith("</RadzenLabel>")) return true;
        return before.EndsWith("/>") && LastTagOpensWith(raw, controlStart, "<RadzenLabel");
    }

    // The very next tag after the control (only whitespace in between) opens a <RadzenLabel>.
    private static bool FollowedByRadzenLabel(string raw, int controlEnd)
    {
        var afterStart = controlEnd + 1;
        var nextLt = raw.IndexOf('<', afterStart);
        if (nextLt < 0) return false;
        if (!string.IsNullOrWhiteSpace(raw.Substring(afterStart, nextLt - afterStart))) return false;
        return raw.AsSpan(nextLt).StartsWith("<RadzenLabel");
    }

    // Finds the tag whose '/>' or '>' ends right before controlStart (ignoring trailing whitespace)
    // and checks whether it opens with the given tag name.
    private static bool LastTagOpensWith(string raw, int controlStart, string tagOpen)
    {
        var before = raw.AsSpan(0, controlStart);
        var trimmedEnd = before.Length;
        while (trimmedEnd > 0 && char.IsWhiteSpace(before[trimmedEnd - 1])) trimmedEnd--;
        if (trimmedEnd == 0) return false;
        var lastLt = raw.LastIndexOf('<', trimmedEnd - 1);
        if (lastLt < 0) return false;
        return raw.AsSpan(lastLt).StartsWith(tagOpen);
    }

    // A toggle is "wrapped" when the nearest still-open ancestor tag is <RadzenFormField ... Text="...">
    // with no intervening </RadzenFormField> between that opening tag and the control - i.e. the field
    // supplies a label attribute directly above the control, which is the broken pattern for toggles.
    private static bool WrappedByFormFieldWithText(string raw, int controlStart)
    {
        var searchEnd = controlStart;
        var openIdx = raw.LastIndexOf("<RadzenFormField", searchEnd, StringComparison.Ordinal);
        if (openIdx < 0) return false;

        var closeIdx = raw.LastIndexOf("</RadzenFormField>", searchEnd, StringComparison.Ordinal);
        if (closeIdx > openIdx) return false; // the enclosing FormField already closed before the control

        var tagEnd = FindTagEnd(raw, openIdx);
        if (tagEnd < 0) return false;
        var openingTag = raw.Substring(openIdx, tagEnd - openIdx + 1);
        return openingTag.Contains("Text=", StringComparison.Ordinal);
    }

    // Walks from the tag open to the '>' that closes the opening tag, skipping any '>'
    // that sits inside a quoted attribute value (e.g. the arrow in a => lambda).
    private static int FindTagEnd(string source, int start)
    {
        var inQuote = false;
        for (var i = start; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"') inQuote = !inQuote;
            else if (c == '>' && !inQuote) return i;
        }
        return -1;
    }

    private static string StripRazorComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(RadzenLabelAssociationAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
