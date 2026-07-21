// SPDX-License-Identifier: EUPL-1.2
using System.IO;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// S-TECH-EMDG: the project forbids the em-dash character (U+2014, "—") in user-facing text. The
/// user-facing string literals were swept once; this guard keeps them swept. It fails the build if a
/// double-quoted string literal in production C#/Razor contains a raw em-dash - a task label, toast,
/// confirm message, or badge text that would surface the forbidden glyph to a human.
///
/// Deliberately narrow to avoid false positives: it inspects only <b>double-quoted string literals</b>,
/// skipping line comments (<c>//</c>, <c>///</c>), block comments (<c>/* */</c>) and Razor comments
/// (<c>@* *@</c>), where an em-dash is a code annotation, not output. It does NOT police Razor markup
/// prose (not a string literal) - that is handled by review, not this guard.
/// </summary>
public sealed class EmDashUserFacingStringAuditTests
{
    private const char EmDash = '—';

    [Fact]
    public void No_user_facing_string_literal_contains_an_em_dash()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            (Path.Combine(repoRoot, "src", "Aetheus.Front"), new[] { "*.razor", "*.cs" }),
            (Path.Combine(repoRoot, "src", "Aetheus.Back", "Components"), new[] { "*.cs" }),
        };

        var violations = new List<string>();
        foreach (var (dir, patterns) in roots)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in patterns)
                foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    if (IsGenerated(file)) continue;
                    var inBlockComment = false;
                    var lines = File.ReadAllLines(file);
                    for (var i = 0; i < lines.Length; i++)
                        if (LineHasEmDashInStringLiteral(lines[i], ref inBlockComment))
                            violations.Add($"  - {Path.GetRelativePath(repoRoot, file)}:{i + 1}");
                }
        }

        Assert.True(violations.Count == 0,
            "User-facing string literals must not contain the em-dash — (use '-', ':', or parentheses). "
            + $"Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool IsGenerated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.EndsWith(".g.cs", StringComparison.Ordinal)
        || file.EndsWith(".razor.g.cs", StringComparison.Ordinal);

    // Single pass over one line: tracks whether we are inside a double-quoted string literal, a block
    // comment, or reached a line comment, and reports an em-dash only when it sits inside a string literal.
    private static bool LineHasEmDashInStringLiteral(string line, ref bool inBlockComment)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("@*", StringComparison.Ordinal)) return false; // Razor comment line

        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (inStr)
            {
                if (c == '\\' && i + 1 < line.Length) { i++; continue; } // skip escaped char
                if (c == '"') { inStr = false; continue; }
                if (c == EmDash) return true;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') return false; // rest is a line comment
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') { inBlockComment = true; i++; }
        }
        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(EmDashUserFacingStringAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
