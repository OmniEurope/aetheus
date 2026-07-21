// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards against visual and accessibility regressions on <c>RadzenButton</c>, scanning the raw
/// <c>.razor</c> markup (same source-text approach as the other front guards).
///
/// 1. <see cref="Buttons_DoNotUse_DeprecatedMaterialSymbolSuffix"/> - Radzen 10 ships
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

    [Fact]
    public void Buttons_DoNotUse_DeprecatedMaterialSymbolSuffix()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRadzenButtonTags())
        {
            var icon = AttributeValue(tag, "Icon");
            if (icon is null) continue;

            foreach (var suffix in DeprecatedIconSuffixes)
            {
                if (icon.EndsWith(suffix, StringComparison.Ordinal))
                {
                    violations.Add($"{file}: Icon=\"{icon}\" uses deprecated suffix '{suffix}' "
                        + "(renders leftover text in Radzen 10 Material Symbols)");
                    break;
                }
            }
        }

        Assert.True(violations.Count == 0,
            "RadzenButton icons must use bare Material Symbols names (Radzen 10) - drop the suffix:\n  "
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

    // ── tag extraction (quote-aware so Click="@(() => …)" lambdas don't truncate the tag) ──

    private static IEnumerable<(string File, string Tag)> EnumerateRadzenButtonTags()
    {
        var razorDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(razorDir), $"Front dir not found: {razorDir}");

        foreach (var file in Directory.EnumerateFiles(razorDir, "*.razor", SearchOption.AllDirectories))
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(RadzenButtonIconAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
