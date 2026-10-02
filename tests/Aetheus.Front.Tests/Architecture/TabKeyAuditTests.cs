// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// An OmniTabsItem without Key is identified by its Title. A title computed at render ("Catalog (12)")
/// changes with its count, the selected tab then matches no item and the panel goes blank: typing a
/// search on /pipelines emptied the page. A computed title therefore needs a stable Key.
/// </summary>
public sealed partial class TabKeyAuditTests
{
    [GeneratedRegex(@"<OmniTabsItem\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TabItemTag();

    [Fact]
    public void EveryTabWithAComputedTitle_HasAStableKey()
    {
        var root = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");
        var offenders = RepositoryScan.Enumerate(root, "*.razor")
            .SelectMany(file => TabItemTag().Matches(File.ReadAllText(file))
                .Select(match => (File: Path.GetRelativePath(root, file), Tag: match.Value)))
            .Where(tab => Regex.IsMatch(tab.Tag, @"\bTitle=""@\(") && !Regex.IsMatch(tab.Tag, @"\bKey="""))
            .Select(tab => $"{tab.File}: {tab.Tag}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A tab whose title is computed must declare Key:\n" + string.Join("\n", offenders));
    }
}
