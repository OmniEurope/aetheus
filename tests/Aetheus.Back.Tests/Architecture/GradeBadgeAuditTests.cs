// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-003 lot 18: one rendering for an analysis grade. Project tiles drew a coloured
/// <c>&lt;strong&gt;</c> through a <c>GradeCss</c> helper whose scale said C is informational, while
/// the pipeline pages drew a badge whose scale says C is a warning: the same letter read differently
/// depending on the page. Everything goes through the <c>GradeBadge</c> component now, the only
/// caller of <c>GetGradeBadge</c>, and the old colour scale is gone.
/// </summary>
public sealed class GradeBadgeAuditTests
{
    /// <summary>Markers of a grade drawn outside the shared component.</summary>
    private static readonly string[] HandRolledGradeMarkers = ["GetGradeBadge", "GradeCss", "project-gate-grade"];

    [Fact]
    public void Only_GradeBadge_Draws_A_Grade()
    {
        var offenders = FrontFiles()
            .Where(file => !file.Name.StartsWith("GradeBadge", StringComparison.Ordinal)
                && !file.Name.Equals("PipelineHelper.cs", StringComparison.Ordinal))
            .Where(file => HandRolledGradeMarkers.Any(marker => file.Text.Contains(marker, StringComparison.Ordinal)))
            .Select(file => file.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files draw a grade themselves instead of using <GradeBadge>: {string.Join(", ", offenders)}");
    }

    /// <summary>Every front source file, with the floor that refuses an empty scan.</summary>
    private static IEnumerable<(string Name, string Text)> FrontFiles()
    {
        var root = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");
        return RepositoryScan.Enumerate(root, "*.razor")
            .Concat(RepositoryScan.Enumerate(root, "*.cs"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)));
    }
}
