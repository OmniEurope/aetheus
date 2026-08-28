// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Prevents browser-culture decimal separators from corrupting interpolated SVG path and
/// transform coordinates. Each sink must be the direct argument of an invariant formatter.
/// </summary>
public sealed class SvgCultureInvariantAuditTests
{
    private static readonly Regex InterpolatedStringRegex = new(
        @"(?:\$@|@\$|\$)""(?:\\.|""""|[^""])*""",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SvgSinkRegex = new(
        @"(?:(?<![A-Za-z0-9])[MLCQTSAHV]\s+\{[^}]*\}\s+\{|(?:translate|rotate|scale|matrix)\(\s*\{)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DirectInvariantCallRegex = new(
        @"(?:\b(?:System\.)?FormattableString\.Invariant|\bFmt)\s*\(\s*$",
        RegexOptions.Compiled);
    private static readonly Regex InvariantFmtDefinitionRegex = new(
        @"\b(?:static\s+)?string\s+Fmt\s*\(\s*FormattableString\b",
        RegexOptions.Compiled);

    [Fact]
    public void Svg_coordinate_interpolation_uses_invariant_culture()
    {
        var repoRoot = FindRepoRoot();
        var frontDir = Path.Combine(repoRoot, "src", "Aetheus.Front");
        var violations = new List<string>();
        var sinksScanned = 0;

        foreach (var pattern in new[] { "*.cs", "*.razor" })
            foreach (var file in RepositoryScan.Enumerate(frontDir, pattern))
            {
                if (IsGenerated(file)) continue;
                var source = File.ReadAllText(file);
                var fileViolations = FindUnprotectedSinks(source, out var fileSinks);
                sinksScanned += fileSinks;
                violations.AddRange(fileViolations.Select(index =>
                    $"{Path.GetRelativePath(repoRoot, file)}:{LineNumberAt(source, index)}"));
            }

        Assert.True(sinksScanned >= 3,
            $"Scanner found only {sinksScanned} SVG interpolation sinks - likely a scanner regression.");
        Assert.True(violations.Count == 0,
            "SVG/transform coordinate interpolation must be the direct argument of "
            + "FormattableString.Invariant or Fmt(FormattableString):\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void Scanner_Handles_Multiline_Sinks_And_Ignores_Comment_Decoys()
    {
        const string source = """
            // FormattableString.Invariant($"M {commentX} {commentY}")
            var unsafePath = $"M {x}
                {y}";
            var safePath = FormattableString.Invariant(
                $"M {x}
                    {y}");
            """;

        var violations = FindUnprotectedSinks(source, out var sinksScanned);

        Assert.Equal(2, sinksScanned);
        Assert.Equal(source.IndexOf("$\"M {x}", StringComparison.Ordinal), Assert.Single(violations));
    }

    [Fact]
    public void Scanner_Does_Not_Accept_A_Marker_From_An_Unrelated_Comment()
    {
        const string source = """
            // Fmt(FormattableString) is invariant.
            var path = $"M {x} {y}";
            """;

        Assert.Single(FindUnprotectedSinks(source, out _));
    }

    private static List<int> FindUnprotectedSinks(string source, out int sinksScanned)
    {
        var commentMasked = MaskComments(source);
        var hasInvariantFmt = InvariantFmtDefinitionRegex.IsMatch(commentMasked);
        var violations = new List<int>();
        sinksScanned = 0;

        foreach (var interpolation in InterpolatedStringRegex.Matches(commentMasked).Cast<Match>())
        {
            if (!SvgSinkRegex.IsMatch(interpolation.Value)) continue;
            sinksScanned++;

            var prefixStart = Math.Max(0, interpolation.Index - 160);
            var prefix = commentMasked[prefixStart..interpolation.Index];
            var formatter = DirectInvariantCallRegex.Match(prefix);
            if (formatter.Success
                && (!formatter.Value.TrimStart().StartsWith("Fmt", StringComparison.Ordinal) || hasInvariantFmt))
            {
                continue;
            }

            violations.Add(interpolation.Index);
        }

        return violations;
    }

    private static string MaskComments(string source)
    {
        var masked = new StringBuilder(source);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] is '"' or '\'')
            {
                i = FindLiteralEnd(source, i) - 1;
                continue;
            }
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
            else if (i + 1 < source.Length && source[i] == '@' && source[i + 1] == '*')
            {
                var end = source.IndexOf("*@", i + 2, StringComparison.Ordinal);
                var exclusiveEnd = end < 0 ? source.Length : end + 2;
                Mask(masked, i, exclusiveEnd);
                i = exclusiveEnd - 1;
            }
        }
        return masked.ToString();
    }

    private static int FindLiteralEnd(string source, int quoteIndex)
    {
        var delimiter = source[quoteIndex];
        var verbatim = delimiter == '"' && quoteIndex > 0 && source[quoteIndex - 1] == '@';
        var i = quoteIndex + 1;
        while (i < source.Length)
        {
            if (source[i] == delimiter)
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            if (!verbatim && source[i] == '\\' && i + 1 < source.Length) i += 2;
            else i++;
        }
        return source.Length;
    }

    private static void Mask(StringBuilder source, int start, int exclusiveEnd)
    {
        for (var i = start; i < exclusiveEnd; i++)
            if (source[i] is not ('\r' or '\n')) source[i] = ' ';
    }

    private static int LineNumberAt(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++) if (source[i] == '\n') line++;
        return line;
    }

    private static bool IsGenerated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.EndsWith(".g.cs", StringComparison.Ordinal)
        || file.EndsWith(".razor.g.cs", StringComparison.Ordinal);

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
