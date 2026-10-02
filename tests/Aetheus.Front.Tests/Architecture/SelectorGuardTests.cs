// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Static guards over the E2E test *source* that catch two classes of selector bug
/// which otherwise only surface as an opaque Playwright timeout at run time:
///
/// 1. <b>Malformed union <c>text=</c> selectors</b> - a comma-separated list inside a
///    single <c>text=</c> string (e.g. <c>"text=Two-Factor, text=2FA, text=TOTP"</c>).
///    Playwright treats the whole string as one literal text match and never finds it.
///    This was the real bug fixed in <c>TotpTests</c> (see the comment there); the guard
///    stops it from creeping back in anywhere in the suite.
///
/// 2. <b>Stale CSS-class selectors</b> - every app-owned <c>.some-class</c> the E2E tests
///    target is asserted to exist somewhere in the front markup/CSS under
///    <c>src/Aetheus.Front</c>. A class renamed or deleted in the app but still
///    referenced by a test is a stale selector that would time out; this fails the build
///    instead, pointing at the offending file:line.
///
/// Framework classes with the <c>rz-</c> or <c>omni-</c> prefixes are intentionally excluded:
/// they live in packages, not in <c>src/</c>, so they cannot be resolved against app source.
///
/// The scan reads the .cs files from disk (no Roslyn dependency) and resolves the repo
/// root the same way the other architecture guards do - walk up to <c>Aetheus.slnx</c>.
/// </summary>
public class SelectorGuardTests
{
    // A malformed union text selector: a "text=" with a comma later inside the same quoted
    // string AND a second "text=" before the closing quote. Matching against the quoted
    // literal (no unescaped quote in between) keeps this from spanning unrelated arguments.
    private static readonly Regex MalformedTextUnionRegex = new(
        "\"text=[^\"]*,[^\"]*text=[^\"]*\"",
        RegexOptions.Compiled);

    // A CSS class token inside a double-quoted selector string: ".foo", ".foo-bar".
    // Captures every class so multi-class / descendant selectors (".rz-header .app-title")
    // contribute both tokens. The dot must be preceded by a non-identifier char (or be at the
    // string start) so a C# member access - "obj.Property" - left in an interpolation hole is
    // never mistaken for a CSS class.
    private static readonly Regex ClassTokenRegex = new(
        @"(?<![a-zA-Z0-9_)\]])\.(?<cls>[a-zA-Z_][a-zA-Z0-9_-]*)",
        RegexOptions.Compiled);

    // A C# string-interpolation hole - "{...}" - inside a quoted string carries code, not CSS,
    // so its content is stripped before class-token extraction. Escaped "{{"/"}}" are left alone.
    private static readonly Regex InterpolationHoleRegex = new(
        @"(?<!\{)\{[^{}]*\}(?!\})",
        RegexOptions.Compiled);

    // Only the inside of double-quoted string literals is scanned for class tokens, so a
    // member access like "obj.Property" in code never registers as a CSS class.
    private static readonly Regex QuotedStringRegex = new(
        "\"(?<body>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled);

    private static readonly Regex MarkupClassAttributeRegex = new(
        @"\bclass\s*=\s*\""(?<value>[^\""\r\n]*)\""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssSelectorClassRegex = new(
        @"(?<![a-zA-Z0-9_-])\.(?<cls>[a-zA-Z_][a-zA-Z0-9_-]*)",
        RegexOptions.Compiled);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RazorCommentRegex = new(
        @"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlCommentRegex = new(
        @"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    [Fact]
    public void No_Malformed_Union_Text_Selectors_In_E2E_Source()
    {
        var violations = new List<string>();
        var filesScanned = 0;

        foreach (var file in EnumerateE2ESourceFiles())
        {
            filesScanned++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripLineComment(lines[i]);
                if (MalformedTextUnionRegex.IsMatch(code))
                    violations.Add($"{RelativeToRepo(file)}:{i + 1} - {lines[i].Trim()}");
            }
        }

        Assert.True(filesScanned > 0,
            "Scanner found no E2E .cs source files - the path resolution is broken.");
        Assert.True(violations.Count == 0,
            "Malformed Playwright union text selectors found. A single \"text=A, text=B\" string is "
            + "matched as one literal and never resolves - split into separate locators or use "
            + "GetByText per term:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void Every_App_Owned_Class_Selector_Exists_In_Front_Source()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(frontDir), $"Front source dir not found: {frontDir}");

        var frontClasses = LoadFrontClasses(frontDir);
        Assert.True(frontClasses.Count > 10,
            "Loaded front class catalog is suspiciously small - the front-source scan is broken.");

        var violations = new List<string>();
        var classesChecked = 0;

        foreach (var file in EnumerateE2ESourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var cls in ExtractTargetedClasses(StripLineComment(lines[i])))
                {
                    if (cls.StartsWith("rz-", StringComparison.Ordinal)
                        || cls.StartsWith("omni-", StringComparison.Ordinal)) continue;
                    classesChecked++;

                    if (!frontClasses.Contains(cls))
                        violations.Add($"{RelativeToRepo(file)}:{i + 1} - '.{cls}' not found anywhere in "
                            + "src/Aetheus.Front (stale selector?)");
                }
            }
        }

        Assert.True(classesChecked > 0,
            "No app-owned class selectors were found to check - the extractor is broken.");
        Assert.True(violations.Count == 0,
            "E2E tests target CSS classes that no longer exist in the front app. Rename the test "
            + "selector to match the app, or restore the class:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void FrontClassCatalog_IgnoresCommentsAndUnrelatedStrings()
    {
        var classes = LoadFrontClassesFromSources([
            ("sample.razor", "@* <div class=\"razor-comment\"> *@\n<!-- class=\"html-comment\" -->\n<div class=\"real combined\"></div>"),
            ("sample.css", "/* .css-comment { display:none; } */\n.styled:hover { color: red; }"),
            ("sample.cs", "var unrelated = \"string-only\";")
        ]);

        Assert.Contains("real", classes);
        Assert.Contains("combined", classes);
        Assert.Contains("styled", classes);
        Assert.DoesNotContain("razor-comment", classes);
        Assert.DoesNotContain("html-comment", classes);
        Assert.DoesNotContain("css-comment", classes);
        Assert.DoesNotContain("string-only", classes);
    }

    [Fact]
    public void SelectorExtractor_IgnoresJavaScriptMethodCalls()
    {
        var classes = ExtractTargetedClasses(
            """const ready = await page.locator("[data-viewport='ready']").getAttribute("class");""");

        Assert.DoesNotContain("getAttribute", classes);
    }

    private static IEnumerable<string> ExtractTargetedClasses(string line)
    {
        string[] nonSelectorFileExtensions = ["txt", "log", "png", "jpg", "jpeg", "webp", "json", "trx"];
        foreach (Match str in QuotedStringRegex.Matches(line))
        {
            // Strip C# interpolation holes - "{Page.Url}", "{PlaywrightConfig.BlazorReadyTestId}" -
            // so their member-access dots aren't mistaken for CSS classes. The surviving text is the
            // literal selector content.
            var body = InterpolationHoleRegex.Replace(str.Groups["body"].Value, " ");
            foreach (Match m in ClassTokenRegex.Matches(body))
            {
                var token = m.Groups["cls"].Value;
                if (body[(m.Index + m.Length)..].TrimStart().StartsWith('('))
                    continue;
                if (!nonSelectorFileExtensions.Contains(token, StringComparer.OrdinalIgnoreCase))
                    yield return token;
            }
        }
    }

    // Drops a trailing "//" line comment so documented bug examples (e.g. the old malformed
    // selector preserved as a comment in TotpTests) don't self-trip the scan. A naive split is
    // fine here: the E2E selectors never embed "//" inside a string literal.
    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    private static HashSet<string> LoadFrontClasses(string frontDir)
    {
        var sources = RepositoryScan.Enumerate(frontDir, "*.*")
            .Where(file => Path.GetExtension(file) is ".razor" or ".css" or ".html")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)));
        return LoadFrontClassesFromSources(sources);
    }

    private static HashSet<string> LoadFrontClassesFromSources(IEnumerable<(string Name, string Source)> sources)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, rawSource) in sources)
        {
            var source = HtmlCommentRegex.Replace(RazorCommentRegex.Replace(rawSource, " "), " ");
            if (Path.GetExtension(name).Equals(".css", StringComparison.OrdinalIgnoreCase))
            {
                source = BlockCommentRegex.Replace(source, " ");
                foreach (Match match in CssSelectorClassRegex.Matches(source))
                    classes.Add(match.Groups["cls"].Value);
                continue;
            }

            foreach (Match attribute in MarkupClassAttributeRegex.Matches(source))
            {
                foreach (var token in attribute.Groups["value"].Value.Split(
                             [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!token.Contains('@', StringComparison.Ordinal)
                        && Regex.IsMatch(token, @"^[a-zA-Z_][a-zA-Z0-9_-]*$"))
                    {
                        classes.Add(token);
                    }
                }
            }
        }

        return classes;
    }

    private static IEnumerable<string> EnumerateE2ESourceFiles()
    {
        var e2eDir = Path.Combine(FindRepoRoot(), "tests", "Aetheus.E2E");
        foreach (var file in RepositoryScan.Enumerate(e2eDir, "*.cs"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            // Skip this guard itself - its doc comments/regex literals deliberately contain the
            // malformed-selector and class-token patterns it hunts for, which would self-trip.
            if (Path.GetFileName(file) == "SelectorGuardTests.cs") continue;
            yield return file;
        }
    }

    private static string RelativeToRepo(string path) =>
        Path.GetRelativePath(FindRepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
