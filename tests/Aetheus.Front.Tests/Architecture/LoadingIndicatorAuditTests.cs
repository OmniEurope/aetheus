// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards the product-wide loading identity: indeterminate waits use AetheusLoader (OE's logo
/// loader holding the Aetheus plane), while OE progress bars remain available for determinate progress.</summary>
public sealed class LoadingIndicatorAuditTests
{
    [Fact]
    public void IndeterminateCircularSpinners_AreNotUsed()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.razor"))
        {
            // Recette R-333: the page-load bar under the top bar is the one indeterminate OE bar the user
            // asked for; every wait inside a page still renders AetheusLoader.
            if (Path.GetFileName(file) == "PageLoadProgressBar.razor") continue;
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"<OmniProgressBar\b(?:(?:""[^""]*"")|[^>])*?/>", RegexOptions.CultureInvariant))
            {
                if (match.Value.Contains("Value=", StringComparison.Ordinal)) continue;
                // Recette R2-030 (2026-10-01): the user asked for a bar on a service row while its
                // operation runs. Only that one shape is allowed, a labelled linear bar (nothing spins,
                // decision D11 holds); any other valueless bar in the file is still refused.
                if (Path.GetFileName(file) == "ManageableServiceGrid.razor"
                    && match.Value.Contains("Shape=\"OmniProgressShape.Linear\"", StringComparison.Ordinal)
                    && match.Value.Contains("Label=", StringComparison.Ordinal)) continue;
                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add($"{Path.GetRelativePath(frontDir, file)}:{line}");
            }
        }

        Assert.True(violations.Count == 0,
            "Indeterminate waits must render AetheusLoader instead of a valueless OmniProgressBar:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// Recette R-536 (decision of 2026-10-01): the one indicator is OE's logo loader holding the plane. The
    /// wrapper draws no mark and no motion of its own, every other file goes through it so the logo and
    /// the words stay the product's, and the stylesheet no longer animates a local copy.
    /// </summary>
    [Fact]
    public void AetheusLoader_IsTheOeLogoLoaderHoldingThePlane()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var loaderFile = Path.Combine(frontDir, "Components", "Shared", "AetheusLoader.razor");
        var loader = File.ReadAllText(loaderFile);

        Assert.Matches(@"<OmniLogoLoader\b", loader);
        // R2-075 (OE 1.5.0): the splash's animated plane, played as it is, its motion in CSS keyframes only.
        Assert.Matches(@"<OmniLogoLoader\b[^>]*\bAnimatedMark=""true""", loader);
        Assert.Contains("class=\"p-ship\"", loader, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"<(animate|animateTransform|animateMotion|set)\b", loader);

        var direct = RepositoryScan.Enumerate(frontDir, "*.razor")
            .Where(file => !string.Equals(file, loaderFile, StringComparison.OrdinalIgnoreCase))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"<OmniLogoLoader\b", RegexOptions.CultureInvariant))
            .Select(file => Path.GetRelativePath(frontDir, file))
            .ToList();
        Assert.True(direct.Count == 0,
            "Render AetheusLoader, not OmniLogoLoader directly, so every wait shows the plane and the same words:\n  "
            + string.Join("\n  ", direct));

        var css = File.ReadAllText(Path.Combine(frontDir, "wwwroot", "css", "app.css"));
        Assert.DoesNotMatch(@"@keyframes\s+aetheus-loader", css);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
