// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using System.Text.Json;
using Aetheus.Front.Services;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the contract that every page-key that <see cref="HelpService"/> can route a route to
/// (the <c>Key</c> of each entry in its private <c>PageMappings</c> table) has a matching article
/// id in BOTH <c>wwwroot/help/help-en.json</c> and <c>help-fr.json</c>. A mapping whose key has no
/// article means the contextual-help drawer routes to nothing - a silent "no help found" for that
/// page, in one or both languages, that no other test catches because the lookup just returns null.
///
/// The mappings live in a private static field and the entry is a private nested record, so we read
/// them by reflection rather than duplicate the table here (the table is the single source of truth).
/// The JSON files are read from disk at the repo path, mirroring how the other front archi guards
/// (<c>JsonOptionsCoverageTests</c>) resolve source files relative to the test assembly.
/// </summary>
public class HelpArticleCoverageTests
{
    [Fact]
    public void Every_PageMapping_Key_Has_An_Article_In_Both_Languages()
    {
        var mappingKeys = ReadPageMappingKeys();
        Assert.NotEmpty(mappingKeys); // Reflection sanity: a broken read would silently pass forever.

        var helpDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "help");
        var enKeys = ReadArticleKeys(Path.Combine(helpDir, "help-en.json"));
        var frKeys = ReadArticleKeys(Path.Combine(helpDir, "help-fr.json"));

        var missingEn = mappingKeys.Where(k => !enKeys.Contains(k)).ToList();
        var missingFr = mappingKeys.Where(k => !frKeys.Contains(k)).ToList();

        Assert.True(missingEn.Count == 0 && missingFr.Count == 0,
            "Every HelpService.PageMappings target key must have a matching article id in both "
            + "help-en.json and help-fr.json (otherwise the contextual help drawer routes to nothing):\n"
            + $"  Missing in help-en.json: {FormatKeys(missingEn)}\n"
            + $"  Missing in help-fr.json: {FormatKeys(missingFr)}");
    }

    private static string FormatKeys(IReadOnlyCollection<string> keys) =>
        keys.Count == 0 ? "(none)" : string.Join(", ", keys);

    private static IReadOnlyCollection<string> ReadPageMappingKeys()
    {
        var field = typeof(HelpService).GetField("PageMappings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, "HelpService.PageMappings field not found - was it renamed?");

        var mappings = (System.Collections.IEnumerable)field!.GetValue(null)!;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        PropertyInfo? keyProp = null;
        foreach (var mapping in mappings)
        {
            keyProp ??= mapping.GetType().GetProperty("Key");
            Assert.True(keyProp is not null, "PageMapping.Key property not found - was it renamed?");
            keys.Add((string)keyProp!.GetValue(mapping)!);
        }
        return keys;
    }

    private static IReadOnlyCollection<string> ReadArticleKeys(string jsonPath)
    {
        Assert.True(File.Exists(jsonPath), $"Help file not found: {jsonPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var article in doc.RootElement.EnumerateArray())
        {
            keys.Add(article.GetProperty("key").GetString()!);
        }
        return keys;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(HelpArticleCoverageTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
