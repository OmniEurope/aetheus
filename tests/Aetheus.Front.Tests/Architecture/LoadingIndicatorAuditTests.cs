// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards the product-wide loading identity: indeterminate waits use the animated
/// Aetheus plane, while Radzen's circular control remains available for determinate progress.</summary>
public sealed class LoadingIndicatorAuditTests
{
    [Fact]
    public void IndeterminateCircularSpinners_AreNotUsed()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"<RadzenProgressBarCircular\b[\s\S]*?/>", RegexOptions.CultureInvariant))
            {
                if (!match.Value.Contains("ProgressBarMode.Indeterminate", StringComparison.Ordinal)) continue;
                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add($"{Path.GetRelativePath(frontDir, file)}:{line}");
            }
        }

        Assert.True(violations.Count == 0,
            "Indeterminate waits must render AetheusLoader instead of RadzenProgressBarCircular:\n  "
            + string.Join("\n  ", violations));
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
