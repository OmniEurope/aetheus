// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards against visual and accessibility regressions on <c>RadzenButton</c>, scanning the raw
/// <c>.razor</c> markup (same source-text approach as the other front guards).
///
/// 1. <see cref="AllMaterialSymbolProviders_DoNotUseDeprecatedSuffixes"/> - Radzen 10 ships
///    Material Symbols; the legacy <c>_outline</c>/<c>_rounded</c>/<c>_sharp</c> suffixes
///    no longer resolve and render the raw glyph name as leftover text next to the button.
///    This is a real, previously-observed regression, so it is failed loudly.
///
/// 2. <see cref="TextButtons_CarryAnIcon"/> - every labelled <c>RadzenButton</c> must pair its
///    <c>Text</c> with an <c>Icon</c> so the button reads at a glance. This is the standardised
///    rule (UX decision IC0N-B): there is NO neutral-action allowlist - Cancel/Save/Confirm and
///    friends all carry an icon. Only dynamic labels (ternaries, <c>@svc.Name</c>…) are exempt,
///    since their icon decision lives with the data, not the static markup. The guard fails the
///    build the moment a static-label button appears without an icon.
/// </summary>
public class RadzenButtonIconAuditTests
{
    private static readonly string[] DeprecatedIconSuffixes =
        ["_outline", "_outlined", "_rounded", "_sharp", "_two_tone", "_twotone"];

    private static readonly IReadOnlyDictionary<string, string> CanonicalTableActionIcons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Copy"] = "content_copy",
            ["Delete"] = "delete",
            ["Edit"] = "edit",
            ["Restart"] = "restart_alt",
            ["Start"] = "play_arrow",
            ["Stop"] = "stop"
        };

    [Fact]
    public void AllMaterialSymbolProviders_DoNotUseDeprecatedSuffixes()
    {
        var frontRoot = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var violations = Directory
            .EnumerateFiles(frontRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => DeprecatedIconSuffixes.SelectMany(suffix =>
                Regex.Matches(File.ReadAllText(file), $"\\\"(?<icon>[a-z0-9_]+{Regex.Escape(suffix)})\\\"")
                    .Select(match =>
                        $"{Path.GetRelativePath(frontRoot, file)}: icon '{match.Groups["icon"].Value}' uses deprecated suffix '{suffix}'")))
            .ToList();

        Assert.True(violations.Count == 0,
            "All icon providers must use bare Material Symbols names (Radzen 10) - drop the suffix:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void TextButtons_CarryAnIcon()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRadzenButtonTags())
        {
            // Only enforce when the button is labelled but icon-less.
            if (AttributeValue(tag, "Text") is null) continue;
            if (AttributeValue(tag, "Icon") is not null) continue;

            // We can only reason statically about @L["Key"] labels; dynamic labels
            // (Text="@svc.Name", ternaries…) are exempt - the icon decision for those
            // lives with the data, not the markup.
            var key = LocalizerKey(tag);
            if (key is null) continue;

            violations.Add($"{file}: labelled button Text=\"@L[\"{key}\"]\" has no Icon");
        }

        Assert.True(violations.Count == 0,
            "Every static-labelled RadzenButton must carry an Icon (UX decision IC0N-B - no neutral "
            + "allowlist). Add an Icon to each:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void IconOnlyButtons_HaveAnAccessibleNameAndTooltip()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRadzenButtonTags())
        {
            if (AttributeValue(tag, "Icon") is null) continue;
            if (AttributeValue(tag, "Text") is not null) continue;

            var title = AttributeValue(tag, "Title");
            var ariaLabel = AttributeValue(tag, "aria-label");
            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(ariaLabel)) continue;

            violations.Add($"{file}: icon-only button has neither Title nor aria-label: {tag}");
        }

        Assert.True(violations.Count == 0,
            "Every icon-only RadzenButton must expose an accessible name and tooltip through Title "
            + "(or an explicit aria-label when a tooltip is supplied by its wrapper):\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void LabelledPrimaryActions_AreNotExtraSmall()
    {
        var violations = EnumerateRadzenButtonTags()
            .Where(item => AttributeValue(item.Tag, "Text") is not null)
            .Where(item => string.Equals(AttributeValue(item.Tag, "ButtonStyle"), "ButtonStyle.Primary", StringComparison.Ordinal))
            .Where(item => string.Equals(AttributeValue(item.Tag, "Size"), "ButtonSize.ExtraSmall", StringComparison.Ordinal))
            .Select(item => $"{item.File}: primary labelled action uses ButtonSize.ExtraSmall: {item.Tag}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Labelled primary actions must keep the standard hit target; remove ButtonSize.ExtraSmall:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void DataGridButtons_AreIconActions()
    {
        var violations = EnumerateDataGridButtonTags()
            .Where(item => AttributeValue(item.Tag, "Icon") is null)
            .Select(item => $"{item.File}: table button has no Icon: {item.Tag}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Every button rendered in a data-grid cell must be an icon action:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void RecurringTableActions_UseCanonicalIcons()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateDataGridButtonTags())
        {
            var key = LocalizerKey(tag);
            if (key is null || !CanonicalTableActionIcons.TryGetValue(key, out var expectedIcon)) continue;

            var icon = AttributeValue(tag, "Icon");
            if (!string.Equals(icon, expectedIcon, StringComparison.Ordinal))
                violations.Add($"{file}: {key} must use Icon=\"{expectedIcon}\", found Icon=\"{icon}\"");
        }

        Assert.True(violations.Count == 0,
            "Recurring table actions must use the same canonical icon everywhere:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void DataGridCss_KeepsActionsCompactAndIconOnly()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        Assert.Contains(".rz-datatable .rz-data-row .rz-button .rz-button-text", css);
        Assert.Contains("width: var(--aetheus-grid-row-content-height);", css);
        Assert.Contains("font-size: 0.875rem;", css);
        Assert.Contains("border: 1px solid color-mix", css);
        Assert.Contains("margin-inline: 0.125rem;", css);
    }

    [Fact]
    public void EveryRenderedDataGridButton_ReceivesATooltip()
    {
        var root = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Front", "wwwroot", "js", "layout.js"));
        var index = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Front", "wwwroot", "index.html"));

        Assert.Contains(".rz-datatable button", script, StringComparison.Ordinal);
        Assert.Contains("button.querySelector('.rz-button-text')", script, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('aria-label')", script, StringComparison.Ordinal);
        Assert.Contains("button.setAttribute('title', label)", script, StringComparison.Ordinal);
        Assert.Contains("new MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("js/layout.js?v=4", index, StringComparison.Ordinal);
    }

    // ── tag extraction (quote-aware so Click="@(() => …)" lambdas don't truncate the tag) ──

    private static IEnumerable<(string File, string Tag)> EnumerateRadzenButtonTags()
    {
        var razorDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(razorDir), $"Front dir not found: {razorDir}");

        foreach (var file in RepositoryScan.Enumerate(razorDir, "*.razor"))
        {
            var raw = StripRazorComments(File.ReadAllText(file));
            var rel = Path.GetRelativePath(razorDir, file);

            var idx = 0;
            while ((idx = raw.IndexOf("<RadzenButton", idx, StringComparison.Ordinal)) >= 0)
            {
                var end = FindTagEnd(raw, idx);
                if (end < 0) break;
                yield return (rel, raw.Substring(idx, end - idx + 1));
                idx = end + 1;
            }
        }
    }

    private static IEnumerable<(string File, string Tag)> EnumerateDataGridButtonTags()
    {
        var razorDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(razorDir), $"Front dir not found: {razorDir}");

        foreach (var file in RepositoryScan.Enumerate(razorDir, "*.razor"))
        {
            var raw = StripRazorComments(File.ReadAllText(file));
            var rel = Path.GetRelativePath(razorDir, file);

            foreach (Match column in Regex.Matches(
                raw,
                @"<RadzenDataGridColumn\b.*?</RadzenDataGridColumn>",
                RegexOptions.Singleline))
            {
                var idx = 0;
                while ((idx = column.Value.IndexOf("<RadzenButton", idx, StringComparison.Ordinal)) >= 0)
                {
                    var end = FindTagEnd(column.Value, idx);
                    if (end < 0) break;
                    yield return (rel, column.Value.Substring(idx, end - idx + 1));
                    idx = end + 1;
                }
            }
        }
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

    private static string? AttributeValue(string tag, string name)
    {
        var m = Regex.Match(
            tag,
            $@"\b{Regex.Escape(name)}\s*=\s*""(?<v>(?:[^""\\]|\\.)*)""",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["v"].Value : null;
    }

    private static string? LocalizerKey(string tag)
    {
        // Read the @L["Key"] label straight from the tag. The generic AttributeValue helper can't:
        // the nested quotes in Text="@L["Key"]" terminate its value capture at the first inner ".
        var m = Regex.Match(tag, @"\bText\s*=\s*""@L\[""(?<k>[^""]+)""\]""");
        return m.Success ? m.Groups["k"].Value : null;
    }

    private static string StripRazorComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
