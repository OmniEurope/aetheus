// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// A page has one title, the one its <c>OmniPageHeader</c> renders (PLAN-003 D2). The shared lists
/// (pipelines, releases, environments, libraries, vaults) each had their own title in the toolbar
/// they draw in global mode, and every global page that hosts one also renders an <c>OmniPageHeader</c>:
/// /pipelines read "Pipelines" twice, and so did four other pages (PLAN-004 R-04).
/// </summary>
public sealed class ListToolbarTitleAuditTests
{
    private static readonly Regex Toolbar = new(
        """<div class="pipeline-list-toolbar">(?<body>.*?)</Omni(?:Stack|Row)>""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Heading = new(
        """TextStyle="TextStyle\.H[1-6]"|<h[1-6][\s>]|<OmniHeading\b""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ListToolbars_CarryNoTitleOfTheirOwn()
    {
        var front = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");
        var toolbars = 0;
        var violations = new List<string>();
        foreach (var file in RepositoryScan.Enumerate(front, "*.razor"))
        {
            foreach (Match toolbar in Toolbar.Matches(File.ReadAllText(file)))
            {
                toolbars++;
                if (Heading.IsMatch(toolbar.Groups["body"].Value))
                    violations.Add(Path.GetRelativePath(front, file));
            }
        }

        // Recette R-218 moved the create actions of four lists into the page header, so fewer toolbars remain;
        // recette R-416 moved the releases help link into its page header, which removed that toolbar too.
        // The count only proves the scan still finds the remaining toolbars (Git repositories, templates).
        Assert.True(toolbars >= 2, $"Expected the shared list toolbars to be found, got {toolbars}.");
        Assert.True(violations.Count == 0,
            "A list toolbar repeats the page title that OmniPageHeader already renders:\n"
            + string.Join("\n", violations));
    }
}
