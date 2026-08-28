// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Architectural guard: each test file declares at most one top-level public class/record. Audit
/// 360 lot H split the multi-class test files that had accreted; this keeps them split so a single
/// file can't silently grow several unrelated test classes again (which hides size, blurs ownership
/// and breaks the one-test-class-per-file convention).
/// </summary>
public partial class OneClassPerFileAuditTests
{
    [GeneratedRegex(@"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|record)\s+\w+", RegexOptions.Multiline)]
    private static partial Regex TopLevelPublicTypeRegex();

    [Fact]
    public void EveryTestFile_DeclaresAtMostOnePublicType()
    {
        var testsDir = Path.Combine(FindRepoRoot(), "tests", "Aetheus.Front.Tests");
        Assert.True(Directory.Exists(testsDir), $"Tests dir not found: {testsDir}");

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in RepositoryScan.Enumerate(testsDir, "*.cs"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            scanned++;
            var count = TopLevelPublicTypeRegex().Matches(File.ReadAllText(file)).Count;
            if (count > 1)
                offenders.Add($"{Path.GetFileName(file)} ({count} public types)");
        }

        Assert.True(scanned >= 50, $"Scanner found only {scanned} test files - likely a path bug.");
        Assert.True(offenders.Count == 0,
            "Each test file must declare at most one top-level public class/record (audit 360 lot H). "
            + "Split these: " + string.Join(", ", offenders));
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
