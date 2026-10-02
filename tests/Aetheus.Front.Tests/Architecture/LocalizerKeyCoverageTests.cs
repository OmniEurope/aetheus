// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// A key read through the localizer (<c>L["Key"]</c>) that no resource file declares is shown to the user
/// as its raw key: the 2026-09-28 recette found "Line 631" on a French finding page, and seven other
/// buttons, labels and validation messages in the same state. Every literal key read in the front must
/// exist in both the English and the French resource files, and the two files declare the same keys.
/// </summary>
public class LocalizerKeyCoverageTests
{
    private static readonly Regex LocalizerRead = new(@"\bL\[""(?<key>[A-Za-z0-9_.]+)""\]", RegexOptions.CultureInvariant);

    private static readonly Regex ResourceKey = new(@"<data name=""(?<key>[^""]+)""", RegexOptions.CultureInvariant);

    private static string FrontRoot => Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");

    private static HashSet<string> ResourceKeys(string fileName) =>
        ResourceKey.Matches(File.ReadAllText(Path.Combine(FrontRoot, "Resources", fileName)))
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryLocalizerKeyReadInTheFront_ExistsInBothLanguages()
    {
        var english = ResourceKeys("AppStrings.resx");
        var french = ResourceKeys("AppStrings.fr-FR.resx");

        var missing = RepositoryScan.Enumerate(FrontRoot, "*.razor", SearchOption.AllDirectories)
            .Concat(RepositoryScan.Enumerate(FrontRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => LocalizerRead.Matches(File.ReadAllText(path))
                .Select(match => (Key: match.Groups["key"].Value, File: Path.GetRelativePath(FrontRoot, path))))
            .Where(read => !english.Contains(read.Key) || !french.Contains(read.Key))
            .Select(read => $"{read.Key} ({read.File}){(english.Contains(read.Key) ? "" : " [en]")}{(french.Contains(read.Key) ? "" : " [fr]")}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "Localizer keys without a resource:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void TheEnglishAndFrenchResourceFiles_DeclareTheSameKeys()
    {
        var english = ResourceKeys("AppStrings.resx");
        var french = ResourceKeys("AppStrings.fr-FR.resx");

        Assert.Empty(english.Except(french, StringComparer.Ordinal));
        Assert.Empty(french.Except(english, StringComparer.Ordinal));
    }
}
