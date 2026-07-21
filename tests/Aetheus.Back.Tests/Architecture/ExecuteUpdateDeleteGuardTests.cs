// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Guards that every <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> call in
/// repository files is wrapped in an <c>if (db.Database.IsRelational())</c> check.
/// The InMemory EF Core provider does not support these bulk operations and throws
/// at runtime (500) - the guard ensures a fallback path exists for unit tests.
/// </summary>
public class ExecuteUpdateDeleteGuardTests
{
    private static readonly Regex BulkCallRegex = new(
        @"\b(ExecuteUpdateAsync|ExecuteDeleteAsync)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void Every_ExecuteUpdateDelete_Call_Must_Be_Guarded_By_IsRelational()
    {
        var componentsDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Back", "Components");
        Assert.True(Directory.Exists(componentsDir), $"Components dir not found: {componentsDir}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(componentsDir, "*.cs", SearchOption.AllDirectories))
        {
            var raw = File.ReadAllText(file);
            var stripped = StripCommentsAndStrings(raw);

            foreach (var match in BulkCallRegex.Matches(stripped).Cast<Match>())
            {
                // Walk backwards looking for IsRelational() in the containing block.
                // Generous lookback: server-delete cleans 16+ tables under a single guard.
                var start = Math.Max(0, match.Index - 3000);
                var context = stripped[start..match.Index];

                if (context.Contains("IsRelational()")) continue;

                var line = LineNumberAt(stripped, match.Index);
                violations.Add($"{Path.GetFileName(file)}:{line} - {match.Groups[1].Value} missing IsRelational() guard");
            }
        }

        Assert.True(violations.Count == 0,
            "ExecuteUpdateAsync/ExecuteDeleteAsync calls must be wrapped in "
            + "if (db.Database.IsRelational()) { } else { /* InMemory fallback */ }:\n  "
            + string.Join("\n  ", violations));
    }

    private static int LineNumberAt(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++) if (source[i] == '\n') line++;
        return line;
    }

    private static string StripCommentsAndStrings(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        var len = source.Length;
        while (i < len)
        {
            if (i + 1 < len && source[i] == '/' && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                for (var j = i; j < end + 2; j++) if (source[j] == '\n') sb.Append('\n');
                i = end + 2;
                continue;
            }
            if (i + 1 < len && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < len && source[i] != '\n') i++;
                continue;
            }
            if (source[i] == '"')
            {
                i++;
                while (i < len && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < len) { i += 2; continue; }
                    if (source[i] == '\n') sb.Append('\n');
                    i++;
                }
                if (i < len) i++;
                continue;
            }
            sb.Append(source[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ExecuteUpdateDeleteGuardTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
