// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// A date shown on screen follows the user's culture (recette 2026-10-02: a French user read chart axes
/// as "09-03" and expiry dates as "2026-10-05"). A custom pattern that fixes the year-month-day order,
/// <c>yyyy-MM-dd</c> or <c>MM-dd</c>, ignores that culture; standard formats ("d", "g", "G", "F") and
/// <c>DateDisplay</c> do not. Machine values keep ISO with the invariant culture on the same line, and
/// the files that only build machine values are listed below.
/// </summary>
public sealed class DateDisplayCultureAuditTests
{
    private static readonly Regex FixedOrderPattern = new(@"yyyy-MM-dd|""MM-dd|\{0:MM-dd|:MM-dd", RegexOptions.Compiled);

    // File names, grid filter values and telemetry exports: read by programs, never by the user.
    private static readonly string[] MachineValueFiles =
    [
        "ExportFileNames.cs",
        "GridColumnFilters.cs",
        "AppTelemetryMarkdownExport.cs",
        "DateDisplay.cs",
    ];

    [Fact]
    public void ScreensNeverFixTheDateOrder()
    {
        var violations = new List<string>();
        foreach (var file in RepositoryScan.Enumerate(Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front", "Components"), "*.*")
                     .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
                     .Where(path => !MachineValueFiles.Contains(Path.GetFileName(path))))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (FixedOrderPattern.IsMatch(line) && !line.Contains("InvariantCulture", StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(RepositoryScan.Root, file)}:{index + 1}");
            }
        }

        Assert.True(violations.Count == 0,
            "Format displayed dates in the user's culture (\"d\", \"g\", \"F\" or DateDisplay), not a fixed order: "
            + string.Join(", ", violations));
    }
}
