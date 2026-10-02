// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-003 D11: one loading indicator, and nothing rotates. Three styles used to coexist (the library's
/// spinning grid icon, indeterminate progress bars, AetheusLoader) plus three rotating icons; when
/// the icon font failed to load, the spinner rendered as the word "refresh" turning in the middle of
/// a table. The plane oscillates, it does not spin.
/// </summary>
public sealed class LoadingIndicatorAuditTests
{
    [Fact]
    public void No_Indeterminate_Progress_Bar_Is_Used_As_A_Loader()
    {
        var offenders = FrontRazorFiles()
            .Where(file => file.Text.Contains("ProgressBarMode.Indeterminate", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These use an indeterminate bar where AetheusLoader is the app's loading indicator: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Nothing_Rotates_In_The_Stylesheet()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        Assert.DoesNotContain("rotate(360deg)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@keyframes spin", css, StringComparison.Ordinal);

        // R-260: the shared loading plane must not hide the empty states of every other grid.
        Assert.Contains(".omni-data-grid__state", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\.omni-data-grid__state\s*\{[^}]*display\s*:\s*none", css);
    }

    private static IEnumerable<(string Name, string Text)> FrontRazorFiles() =>
        RepositoryScan.Enumerate(Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front"), "*.razor")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)));
}
