// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// A360-69. Ninety-seven front tests were named <c>_DoesNotThrow</c>, <c>_DoesNothing</c> or
/// <c>_NoOps</c> while asserting something specific - a navigation, a request, a markup change. The
/// name invites the next author to write another one with no assertion at all, since that is what the
/// name promises.
///
/// Seventy were renamed after what they actually assert. The rest were left alone on purpose: a test
/// that only checks a <c>Dispose</c> does not throw is honestly named, and renaming it would be the
/// same lie pointing the other way. This guard keeps that distinction: a test may carry one of these
/// names ONLY if the absence of an exception really is all it asserts.
/// </summary>
public sealed class TestNameHonestyAuditTests
{
    private static readonly Regex MisleadingTestName = new(
        @"public\s+(?:async\s+Task|void)\s+(\w+_(?:DoesNotThrow|DoesNothing|NoOps))\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Assertions compatible with "nothing happened": they observe absence, so they do not contradict
    /// the name. Anything else asserts a positive outcome the name hides.
    /// </summary>
    private static readonly string[] AbsenceAssertions =
    [
        "Assert.Null(",
        "Assert.Empty(",
        "Assert.False(",
        "Assert.DoesNotContain(",
        "Assert.Same(",
        "Assert.Equal("
    ];

    [Fact]
    public void ATestNamedAfterDoingNothing_AssertsNothingMore()
    {
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(
                     Path.Combine(RepositoryScan.Root, "tests", "Aetheus.Front.Tests"), "*.cs"))
        {
            if (Path.GetFileName(file) == nameof(TestNameHonestyAuditTests) + ".cs") continue;

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = MisleadingTestName.Match(lines[index]);
                if (!match.Success) continue;

                foreach (var assertion in AssertionsOf(lines, index))
                {
                    if (AbsenceAssertions.Any(allowed => assertion.Contains(allowed, StringComparison.Ordinal)))
                        continue;
                    violations.Add(
                        $"{Path.GetFileName(file)}:{index + 1} {match.Groups[1].Value} asserts "
                        + $"'{assertion.Trim()}'");
                    break;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "These tests are named after doing nothing but assert a positive outcome. Name them after "
            + "what they verify (Method_Scenario_Expectation), or the next author will read the name "
            + "and write one with no assertion at all:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations));
    }

    private static IEnumerable<string> AssertionsOf(string[] lines, int declarationIndex)
    {
        for (var index = declarationIndex + 1; index < Math.Min(declarationIndex + 45, lines.Length); index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("[Fact]", StringComparison.Ordinal)
                || line.TrimStart().StartsWith("[Theory]", StringComparison.Ordinal)) yield break;
            if (line.Contains("Assert.", StringComparison.Ordinal)) yield return line;
        }
    }
}
