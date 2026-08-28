// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the family of render-tautology fillers that the S-TECH-7K2P sweep eradicated from the
/// whole front suite. After a successful Render, <c>cut</c> and <c>cut.Instance</c> are non-null and
/// <c>cut.Markup</c> is non-empty by construction, so none of these assertions verify behaviour. This
/// guard fails the build the moment any reappears - pushing the next author toward a real behavioural
/// assertion (rendered markup, a recorded HTTP request, mutated state, a JS invocation…).
///
/// SCOPE: covers the four known filler shapes - <c>Assert.NotNull(cut.Instance)</c>,
/// <c>Assert.NotNull(cut)</c>, <c>Assert.NotEmpty(cut.Markup)</c>, and
/// <c>Assert.True(cut.Markup.Length &gt; …)</c>. Same source-text scanning approach as the other
/// front guards.
/// </summary>
public class WeakAssertionAuditTests
{
    // Each entry: the regex that matches the filler, and a human label for the failure message.
    private static readonly (Regex Pattern, string Label)[] WeakAssertions =
    [
        (new(@"Assert\.NotNull\(\s*cut\.Instance\s*\)", RegexOptions.Compiled), "Assert.NotNull(cut.Instance)"),
        (new(@"Assert\.NotNull\(\s*cut\s*\)", RegexOptions.Compiled), "Assert.NotNull(cut)"),
        (new(@"Assert\.NotEmpty\(\s*cut\.Markup\s*\)", RegexOptions.Compiled), "Assert.NotEmpty(cut.Markup)"),
        (new(@"Assert\.True\(\s*cut\.Markup\.Length\s*>", RegexOptions.Compiled), "Assert.True(cut.Markup.Length > …)"),
    ];

    [Fact]
    public void No_Test_Uses_RenderTautologyFiller()
    {
        var testsDir = Path.Combine(FindRepoRoot(), "tests", "Aetheus.Front.Tests");
        Assert.True(Directory.Exists(testsDir), $"Tests dir not found: {testsDir}");

        var offenders = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(testsDir, "*.cs"))
        {
            // Skip this guard's own source - it necessarily contains the patterns in its regexes.
            if (Path.GetFileName(file) == "WeakAssertionAuditTests.cs") continue;

            var source = File.ReadAllText(file);
            var masked = MaskCommentsAndStrings(source);
            foreach (var (pattern, label) in WeakAssertions)
            {
                offenders.AddRange(pattern.Matches(masked).Cast<Match>().Select(match =>
                    $"{Path.GetFileName(file)}:{LineNumberAt(source, match.Index)} - {label}"));
            }
        }

        Assert.True(offenders.Count == 0,
            "Render-tautology fillers prove nothing (cut / cut.Instance are non-null and cut.Markup is "
            + "non-empty after any successful Render). Replace each with a real behavioural assertion - "
            + "rendered markup, a recorded _handler.Requests entry, mutated instance state, or a "
            + "JSInterop.Invocations check:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Scanner_Finds_Multiline_Assertions_And_Ignores_Textual_Decoys()
    {
        const string source = """
            // Assert.NotNull(cut.Instance)
            var decoy = "Assert.NotNull(cut.Instance)";
            Assert.NotNull(
                cut.Instance
            );
            """;

        var masked = MaskCommentsAndStrings(source);
        var matches = WeakAssertions.SelectMany(item => item.Pattern.Matches(masked).Cast<Match>()).ToArray();

        Assert.Single(matches);
        Assert.Equal(3, LineNumberAt(source, matches[0].Index));
    }

    private static int LineNumberAt(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++) if (source[i] == '\n') line++;
        return line;
    }

    private static string MaskCommentsAndStrings(string source)
    {
        var masked = new StringBuilder(source);
        for (var i = 0; i < source.Length; i++)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i + 2);
                var exclusiveEnd = end < 0 ? source.Length : end;
                Mask(masked, i, exclusiveEnd);
                i = exclusiveEnd - 1;
            }
            else if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var exclusiveEnd = end < 0 ? source.Length : end + 2;
                Mask(masked, i, exclusiveEnd);
                i = exclusiveEnd - 1;
            }
            else if (source[i] is '"' or '\'')
            {
                var delimiter = source[i];
                var verbatim = delimiter == '"' && i > 0 && source[i - 1] == '@';
                var end = i + 1;
                while (end < source.Length)
                {
                    if (source[end] == delimiter)
                    {
                        if (verbatim && end + 1 < source.Length && source[end + 1] == '"')
                        {
                            end += 2;
                            continue;
                        }
                        end++;
                        break;
                    }
                    if (!verbatim && source[end] == '\\' && end + 1 < source.Length) end += 2;
                    else end++;
                }
                Mask(masked, i, end);
                i = end - 1;
            }
        }
        return masked.ToString();
    }

    private static void Mask(StringBuilder source, int start, int exclusiveEnd)
    {
        for (var i = start; i < exclusiveEnd; i++)
            if (source[i] is not ('\r' or '\n')) source[i] = ' ';
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
