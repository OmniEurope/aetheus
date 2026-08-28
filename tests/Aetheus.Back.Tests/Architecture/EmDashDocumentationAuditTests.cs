// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// A360-45. The project bans the em dash (U+2014) outright, but the existing guard only inspected
/// string literals in <c>Aetheus.Front</c> and <c>Aetheus.Back/Components</c>. Documentation was
/// therefore unguarded, and that is exactly where the character came back: nine of them landed in the
/// Pipelines README during the blue-green work and nothing turned red.
///
/// This covers the living documentation - the files a contributor writes and a reader reads. It is
/// deliberately NOT repository-wide; the exclusions below are each a record that must not be rewritten,
/// and quietly editing one to satisfy a guard would be worse than the violation.
/// </summary>
public sealed class EmDashDocumentationAuditTests
{
    private const char EmDash = '—';

    /// <summary>
    /// Paths whose em dashes are deliberately left alone, each for a stated reason. This is not a
    /// convenience list: adding to it means arguing that the file is a record rather than living text.
    /// </summary>
    private static readonly (string Path, string Why)[] Exclusions =
    [
        ("docs/security/evidence/",
            "scanner evidence is an immutable record of what a tool actually reported; editing it would "
            + "falsify the evidence"),
        ("docs/plans/",
            "archived plans are a historical record of what was decided at the time, not living text"),
        ("src/Aetheus.Front/wwwroot/css/app.css",
            "pre-existing UI content, out of the audit's scope and tracked separately (A360-73)")
    ];

    [Fact]
    public void No_living_documentation_file_contains_an_em_dash()
    {
        var root = RepositoryScan.Root;
        var violations = new List<string>();

        foreach (var directory in new[] { "src", "deploy", "docs", "scripts", ".pipeline", ".github" })
        {
            var absolute = Path.Combine(root, directory);
            if (!Directory.Exists(absolute)) continue;

            foreach (var file in Directory.EnumerateFiles(absolute, "*.md", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsExcluded(relative)) continue;
                if (relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/node_modules/", StringComparison.Ordinal)) continue;

                var lines = File.ReadAllLines(file);
                for (var index = 0; index < lines.Length; index++)
                    if (lines[index].Contains(EmDash))
                        violations.Add($"{relative}:{index + 1}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "The em dash (U+2014) is banned project-wide; use a comma, a colon or parentheses. Found in:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations));
    }

    /// <summary>
    /// The guard must actually be looking at something. A path typo or a moved directory would
    /// otherwise turn this into a green test that inspects nothing.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyScansTheDocumentation()
    {
        var root = RepositoryScan.Root;
        var scanned = new[] { "src", "deploy", "docs" }
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .Sum(directory => Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).Count());

        Assert.True(scanned > 20, $"Only {scanned} markdown files were found; the scan looks broken.");
    }

    private static bool IsExcluded(string relativePath) =>
        Exclusions.Any(exclusion => relativePath.StartsWith(exclusion.Path, StringComparison.OrdinalIgnoreCase));
}
