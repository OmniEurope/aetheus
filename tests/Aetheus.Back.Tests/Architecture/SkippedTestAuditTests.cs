// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Keeps the suites at zero skipped tests.
///
/// A runtime skip reports a test as "Skipped" in every run: it looks like a deliberate choice while
/// it is really a test that stopped protecting anything, and nobody notices when the condition it
/// waits for never comes back. The two honest alternatives are used instead across the repository:
/// make the dependency injectable (or resolve the tool from PATH) so the test runs everywhere, or
/// declare <c>[Trait("Platform", "windows"|"linux")]</c> so the runner excludes it on the foreign OS
/// and the test does not appear in the results at all.
///
/// This guard is deliberately whitelist-free. Adding an exemption requires deleting that decision
/// from here in the open, which is the point.
/// </summary>
public sealed class SkippedTestAuditTests
{
    private static readonly Regex SkipCall = new(
        @"\bAssert\.Skip(Unless|When)?\s*\(",
        RegexOptions.Compiled);

    // Files whose own source necessarily contains the forbidden text: this guard and its self-test.
    private static readonly string[] SelfReferential = ["SkippedTestAuditTests.cs"];

    [Fact]
    public void NoTestDisablesItselfAtRuntime()
    {
        var suites = Directory.EnumerateDirectories(Path.Combine(Root, "tests"), "Aetheus.*.Tests")
            .ToList();
        Assert.True(suites.Count >= 5, $"Expected the test suites under tests/, found {suites.Count}.");

        var sources = suites
            .SelectMany(suite => RepositoryScan.Enumerate(suite, "*.cs"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !SelfReferential.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .ToList();
        Assert.True(sources.Count >= 400, $"The test-source scan is unexpectedly small ({sources.Count}).");

        var offenders = new List<string>();
        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            offenders.AddRange(SkipCall.Matches(source).Cast<Match>().Select(match =>
                $"{Path.GetFileName(path)}:{LineNumberAt(source, match.Index)}"));
        }

        Assert.True(offenders.Count == 0,
            "A test must not disable itself at runtime. Either make its dependency injectable so it "
            + "runs everywhere, or mark it [Trait(\"Platform\", \"windows\"|\"linux\")] so the runner "
            + "excludes it on the other OS:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void ScannerMatchesEveryFormOfRuntimeSkip()
    {
        const string source = """
            Assert.Skip("plain");
            Assert.SkipUnless(condition, "unless");
            Assert.SkipWhen(condition, "when");
            Assert.True(condition);
            """;

        var matches = SkipCall.Matches(source);

        Assert.Equal(3, matches.Count);
    }

    private static int LineNumberAt(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++) if (source[i] == '\n') line++;
        return line;
    }

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
                directory = directory.Parent;
            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
        }
    }
}
