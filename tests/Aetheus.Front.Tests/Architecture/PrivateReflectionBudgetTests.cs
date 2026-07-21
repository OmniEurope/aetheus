// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Prevents page tests from bypassing component invariants and keeps the remaining private-reflection
/// debt on a monotonic budget. When a test is migrated to bUnit or a public collaborator, lower the
/// corresponding ceiling; increases are not permitted.
/// </summary>
public sealed class PrivateReflectionBudgetTests
{
    private static readonly IReadOnlyDictionary<string, int> MaximumOccurrences =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BindingFlags.NonPublic"] = 879,
            [".GetMethod("] = 921,
            [".GetField("] = 1293,
            [".GetProperty("] = 314
        };

    [Fact]
    public void PageTests_DoNotBypassConstructorsOrIncreasePrivateReflectionDebt()
    {
        var pagesDirectory = Path.Combine(FindRepoRoot(), "tests", "Aetheus.Front.Tests", "Pages");
        var files = Directory.EnumerateFiles(pagesDirectory, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 100, "The page-test source scan is unexpectedly small.");

        var sources = files.Select(File.ReadAllText).ToList();
        var uninitializedObjectUsers = files
            .Where((_, index) => sources[index].Contains("GetUninitializedObject", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(pagesDirectory, file))
            .ToList();
        Assert.True(uninitializedObjectUsers.Count == 0,
            "Page tests must use normal construction, bUnit, or a public collaborator; "
            + "GetUninitializedObject bypasses field initializers and constructor invariants:\n  "
            + string.Join("\n  ", uninitializedObjectUsers));

        var combinedSource = string.Join('\n', sources);
        var violations = MaximumOccurrences
            .Select(budget => new
            {
                budget.Key,
                budget.Value,
                Actual = CountOccurrences(combinedSource, budget.Key)
            })
            .Where(result => result.Actual > result.Value)
            .Select(result => $"{result.Key}: {result.Actual} > budget {result.Value}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Private-reflection debt increased. Migrate the new test to bUnit/public behavior; "
            + "never raise the budget:\n  " + string.Join("\n  ", violations));
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length)
            count++;
        return count;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(PrivateReflectionBudgetTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
