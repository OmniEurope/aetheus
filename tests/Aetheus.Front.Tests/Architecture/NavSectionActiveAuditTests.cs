// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using System.Text.RegularExpressions;
using Aetheus.Front.Layout;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the sidebar's cross-route active-state highlighting. Each top-level nav group
/// in <c>NavMenu.razor</c> renders its children inside an <c>@if (IsOnSection("group"))</c>
/// block and lights up via <c>Selected="@IsOnSection("group")"</c>. The highlight only
/// works if every child route is claimed by that group's entry in <c>IsOnSection</c>'s
/// path table - a child added to the menu but forgotten in the table navigates to a page
/// where the parent group goes dark (the regression this guard prevents).
///
/// The guard derives the (group → child routes) mapping straight from the rendered markup,
/// then exercises the REAL <see cref="NavMenu"/>.<c>IsOnSection</c> logic for each child -
/// so it stays correct when routes are renamed or moved (e.g. under /admin/*), and only
/// breaks when the menu and the active-state table genuinely disagree.
/// </summary>
public class NavSectionActiveAuditTests
{
    [Fact]
    public void Every_Static_Child_Route_Activates_Its_Parent_Group()
    {
        var razor = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "Aetheus.Front", "Layout", "NavMenu.razor"));

        var groups = ExtractGroups(razor);

        // Sanity: the parser must actually find the groups, else a markup change could let
        // this guard pass vacuously.
        Assert.True(groups.Count >= 3,
            $"Expected to parse >=3 nav groups from NavMenu.razor, found {groups.Count}. "
            + "Has the @if (IsOnSection(\"...\")) structure changed?");

        var isOnSection = typeof(NavMenu).GetMethod("IsOnSection",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var currentPathField = typeof(NavMenu).GetField("_currentPath",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var violations = new List<string>();
        var checkedChildren = 0;

        foreach (var (group, childPaths) in groups)
        {
            foreach (var path in childPaths)
            {
                var nav = new NavMenu();
                currentPathField.SetValue(nav, path);
                var active = (bool)isOnSection.Invoke(nav, [group])!;
                checkedChildren++;
                if (!active)
                    violations.Add($"On '/{path}', group '{group}' is NOT active "
                        + "(add it to IsOnSection's section-path table).");
            }
        }

        Assert.True(checkedChildren > 0, "No static child routes were found to check.");
        Assert.True(violations.Count == 0,
            "Nav group highlighting is broken for these child routes:\n  "
            + string.Join("\n  ", violations));
    }

    // Maps each group key to its static (non-parameterized) child Paths, read from the
    // @if (IsOnSection("group")) { ... } block that renders that group's submenu.
    private static List<(string Group, List<string> ChildPaths)> ExtractGroups(string razor)
    {
        var result = new List<(string, List<string>)>();

        foreach (Match m in Regex.Matches(razor, @"IsOnSection\(""(?<g>\w+)""\)\s*\)\s*\{"))
        {
            var group = m.Groups["g"].Value;
            var braceStart = razor.IndexOf('{', m.Index + m.Length - 1);
            if (braceStart < 0) continue;
            var braceEnd = FindMatchingCloseBrace(razor, braceStart);
            if (braceEnd < 0) continue;

            var block = razor.Substring(braceStart, braceEnd - braceStart + 1);

            // Static Path="literal" only - skip interpolated Path="@(...)" routes (their
            // active-state is covered by the parameterized detail-page logic, not the table).
            var children = Regex.Matches(block, @"Path=""(?<p>[^""@][^""]*)""")
                .Select(c => c.Groups["p"].Value)
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (children.Count > 0)
                result.Add((group, children));
        }

        return result;
    }

    private static int FindMatchingCloseBrace(string source, int openIndex)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(NavSectionActiveAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
