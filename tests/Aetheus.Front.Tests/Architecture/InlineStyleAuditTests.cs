// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Keeps presentation in app.css. An inline <c>style="..."</c> cannot be themed, is not reachable by
/// the dark palette, and is invisible to every other audit the project runs.
///
/// This guard matches the HTML attribute only. It deliberately does NOT match Radzen's typed
/// parameters - <c>ButtonStyle=</c>, <c>TextStyle=</c>, <c>BadgeStyle=</c>, <c>AlertStyle=</c> and
/// friends - which are enum bindings, not CSS. That distinction matters: a naive
/// <c>grep 'Style="'</c> reports ~1810 hits on this codebase and every one but four is a Radzen
/// parameter. The phase-10 plan was written against that inflated number.
///
/// The exemptions below are the four real cases, and all four are the same case: a value computed per
/// instance and handed to CSS as a custom property. A class cannot express a per-node coordinate or a
/// per-row fill percentage, so these are correct as written rather than debt to migrate.
/// </summary>
public class InlineStyleAuditTests
{
    /// <summary>
    /// Files allowed to set the style attribute, with the reason. Anything else is a violation.
    /// </summary>
    private static readonly Dictionary<string, string> JustifiedInlineStyles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Pages/Pipelines/StageNode.razor"] =
                "--node-x/--node-y: the graph node's coordinates are computed per node from the layout "
                + "pass; a class cannot carry a per-instance position.",
            ["Pages/Servers/ContactAgentDialog.razor"] =
                "--fill-width: token-budget bar width is a percentage of a live value.",
            ["Layout/ServerDetailLayout.razor"] =
                "Visibility toggled from a null check while the server loads, to hold layout geometry "
                + "without a second render pass.",
            ["Shared/WizardDialog.razor"] =
                "Dialog sizing is interpolated from the wizard's declared step width.",
        };

    // The HTML attribute, not Radzen's typed *Style= parameters: require a non-identifier char before
    // "style", so ButtonStyle=/TextStyle=/BadgeStyle= do not match.
    private static readonly Regex InlineStyleAttribute =
        new(@"(?<![\w-])[Ss]tyle\s*=\s*""", RegexOptions.Compiled);

    [Fact]
    public void NoRazorFile_UsesAnUnjustifiedInlineStyleAttribute()
    {
        var root = FrontRoot();
        var offenders = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(root, "*.razor"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;

            var count = InlineStyleAttribute.Matches(File.ReadAllText(file)).Count;
            if (count == 0) continue;

            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!JustifiedInlineStyles.ContainsKey(rel)) offenders.Add($"  - {rel} ({count})");
        }

        Assert.True(offenders.Count == 0,
            "Inline style attributes belong in app.css as a class (claude-ui-patterns.md). If the value "
            + "is genuinely computed per instance, add the file to JustifiedInlineStyles with the "
            + "reason:" + Environment.NewLine + string.Join(Environment.NewLine, offenders.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryExemption_IsStillUsed()
    {
        var root = FrontRoot();
        var stale = JustifiedInlineStyles.Keys
            .Where(rel =>
            {
                var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(full) || InlineStyleAttribute.Matches(File.ReadAllText(full)).Count == 0;
            })
            .Select(rel => $"  - {rel}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "These files no longer use an inline style - drop their exemption so the list keeps meaning "
            + "something:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    private static string FrontRoot() => Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
