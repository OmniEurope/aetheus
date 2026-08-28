// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Every table in the product offers the same four affordances: sort, resize, advanced filtering and
/// infinite scroll. A user who learns one table has learnt them all, and none of them silently caps
/// what it will show.
///
/// <para>The shared <c>AetheusDataGrid</c> carries all four by default, so a grid built on it needs no
/// declaration. A raw <c>RadzenDataGrid</c> has to opt in explicitly, and this guard is what makes
/// forgetting one impossible: it reads the markup rather than trusting convention.</para>
///
/// <para>Opting out is allowed but never silent: a grid states <c>Virtualize="false"</c> (wrapper) or
/// <c>AllowVirtualization="false"</c> (raw) plus a reason, and is listed here.</para>
/// </summary>
public sealed class GridCapabilityAuditTests
{
    private static readonly string FrontRoot = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");

    /// <summary>The wrapper defines the defaults this guard enforces everywhere else, so it is checked
    /// by its own dedicated test below rather than by the sweep.</summary>
    private const string WrapperFile = "AetheusDataGrid.razor";

    /// <summary>Grids that legitimately show neither a pager nor infinite scroll, because they are
    /// sized to their own content: a dialog listing four parameters gains nothing from a pager and the
    /// control would be pure furniture. Every other table pages. Each entry carries the reason
    /// it does. Anything not on this list must virtualise.</summary>
    private static readonly Dictionary<string, string> PagerExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PipelineRunParametersDialog.razor"] = "Dialog sized to its content: a handful of parameters, no scroll region.",
        ["PipelineRunVariablesDialog.razor"] = "Dialog sized to its content.",
        ["DryRunResultDialog.razor"] = "Dialog sized to its content.",
        ["DockerProjectDialog.razor"] = "Dialog sized to its content.",
        ["VersionHistoryDialog.razor"] = "Dialog sized to its content.",
        ["SecretVersionHistoryDialog.razor"] = "Dialog sized to its content.",
        ["TemplateVersionHistoryDialog.razor"] = "Dialog sized to its content.",
        ["PackageFeedPackagesDialog.razor"] = "Dialog sized to its content.",
        ["PackageRegistryPackageDialog.razor"] = "Dialog sized to its content."
    };

    /// <summary>Finds the opening element only; RadzenDataGridColumn is a different element.</summary>
    private static readonly Regex GridOpen = new(
        @"<RadzenDataGrid(?!Column)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads one opening tag, ending it at the first '&gt;' that is NOT inside a quoted value.
    /// <para>A naive <c>[^&gt;]*&gt;</c> cuts the tag at the arrow of a lambda
    /// (<c>Click="@(() =&gt; Foo())"</c>) and then reports the attributes that follow as missing. That
    /// exact mistake produced a wave of false failures here, so the scan is character-based.</para>
    /// </summary>
    private static IReadOnlyList<string> TagsIn(string text)
    {
        var tags = new List<string>();
        foreach (Match open in GridOpen.Matches(text))
        {
            var index = open.Index + open.Length;
            char? quote = null;
            for (; index < text.Length; index++)
            {
                var current = text[index];
                if (quote is not null)
                {
                    if (current == quote) quote = null;
                    continue;
                }
                if (current is '"' or '\'') { quote = current; continue; }
                if (current == '>') break;
            }
            if (index >= text.Length)
                throw new InvalidOperationException("Unterminated RadzenDataGrid tag.");
            tags.Add(text[open.Index..(index + 1)]);
        }
        return tags;
    }

    private static IReadOnlyList<(string File, string Tag)> RawGrids()
    {
        var results = new List<(string, string)>();
        foreach (var file in RepositoryScan.Enumerate(FrontRoot, "*.razor"))
        {
            if (Path.GetFileName(file).Equals(WrapperFile, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var tag in TagsIn(File.ReadAllText(file)))
                results.Add((Path.GetRelativePath(FrontRoot, file), tag));
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "No raw RadzenDataGrid found. A guard that scans nothing passes vacuously.");
        return results;
    }

    private static bool Declares(string tag, string attribute, string value) =>
        tag.Contains($"{attribute}=\"{value}\"", StringComparison.Ordinal);

    /// <summary>True when the attribute is bound to anything at all (a literal or an expression). A grid
    /// that computes the value per context still made the decision explicitly.</summary>
    private static bool DeclaresAny(string tag, string attribute) =>
        Regex.IsMatch(tag, $@"(?<=[\s<]){Regex.Escape(attribute)}\s*=\s*[""']", RegexOptions.IgnoreCase);

    [Fact]
    public void EveryRawGrid_AllowsSorting()
    {
        var offenders = RawGrids()
            .Where(grid => !DeclaresAny(grid.Tag, "AllowSorting"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table must be sortable. Missing AllowSorting in:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRawGrid_AllowsColumnResize()
    {
        var offenders = RawGrids()
            .Where(grid => !DeclaresAny(grid.Tag, "AllowColumnResize"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table must let the user widen a truncated column. Missing AllowColumnResize in:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRawGrid_UsesAdvancedFiltering()
    {
        var offenders = RawGrids()
            .Where(grid => !DeclaresAny(grid.Tag, "AllowFiltering")
                           || !Declares(grid.Tag, "FilterMode", "FilterMode.Advanced"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table filters through the advanced header menu, the affordance used across the "
            + "product. Missing AllowFiltering or FilterMode.Advanced in:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Every table pages. Virtualization was the default until the cost showed up in use: Radzen can
    /// only virtualise inside a bounded height, so the grid grew its own scroll body while the page
    /// kept its own, and one table carried two scrollbars.
    /// </summary>
    [Fact]
    public void EveryRawGrid_Pages()
    {
        var offenders = RawGrids()
            .Where(grid => !PagerExceptions.ContainsKey(Path.GetFileName(grid.File)))
            .Where(grid => !DeclaresAny(grid.Tag, "AllowPaging"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table pages rather than scrolling infinitely; a virtualised grid needs a bounded "
            + "height and then shows a second scrollbar. Declare AllowPaging in:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The bounded-height class must not survive anywhere: it is what gave a paging grid a second
    /// scrollbar, and it is invisible in review because it sits in a class list.
    /// </summary>
    [Fact]
    public void NoGridKeepsTheBoundedHeightClass()
    {
        var offenders = RepositoryScan.Enumerate(FrontRoot, "*.razor")
            .Where(file => File.ReadAllText(file).Contains("aetheus-grid-virtualized", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(FrontRoot, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "aetheus-grid-virtualized bounds the grid height, which is what produces a second "
            + "scrollbar on a paging table. Remove it from:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The wrapper's defaults are only worth anything if callers keep them. This is the hole the audit
    /// had: it read raw grids only, so a page could hand the wrapper AllowSorting="false" and the suite
    /// stayed green while the table lost its sort and its filter. /pipelines/69 did exactly that.
    /// </summary>
    [Theory]
    [InlineData("AllowSorting")]
    [InlineData("AllowFiltering")]
    [InlineData("AllowColumnResize")]
    public void NoCallerTurnsOffAWrapperCapability(string capability)
    {
        var offenders = new List<string>();
        foreach (var file in RepositoryScan.Enumerate(FrontRoot, "*.razor"))
        {
            if (Path.GetFileName(file).Equals(WrapperFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.ReadAllText(file).Contains($"{capability}=\"false\"", StringComparison.Ordinal))
                offenders.Add(Path.GetRelativePath(FrontRoot, file));
        }

        Assert.True(offenders.Count == 0,
            $"Sorting, filtering and column resize are product-wide affordances; {capability}=\"false\" "
            + "removes one for the reader. Found in:\n" + string.Join("\n", offenders));
    }

    /// <summary>The wrapper is where the defaults live, so they are pinned here: a silent change to any
    /// of them would weaken every grid built on it at once.</summary>
    [Fact]
    public void TheSharedWrapper_CarriesAllFourCapabilitiesByDefault()
    {
        var wrapper = File.ReadAllText(
            Path.Combine(FrontRoot, "Shared", "AetheusDataGrid.razor.cs"));

        Assert.Contains("public bool AllowSorting { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool AllowFiltering { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool AllowColumnResize { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool Virtualize { get; set; } = AetheusGrid.Virtualization;", wrapper, StringComparison.Ordinal);
        // The switch defaults to off: tables page. Turning it back on product-wide would restore the
        // bounded height, and with it the second scrollbar on every grid at once.
        Assert.Contains(
            "public static bool Virtualization { get; set; }",
            File.ReadAllText(Path.Combine(FrontRoot, "Shared", "AetheusGrid.cs")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static bool Virtualization { get; set; } = true;",
            File.ReadAllText(Path.Combine(FrontRoot, "Shared", "AetheusGrid.cs")),
            StringComparison.Ordinal);
        Assert.Contains("FilterMode FilterMode { get; set; } = FilterMode.Advanced;", wrapper, StringComparison.Ordinal);

        var markup = File.ReadAllText(Path.Combine(FrontRoot, "Shared", "AetheusDataGrid.razor"));
        // Paging and virtualization are mutually exclusive in Radzen: binding one to the negation of
        // the other is what keeps the wrapper from silently rendering a pager over a virtualised body.
        Assert.Contains("AllowVirtualization=\"@Virtualize\"", markup, StringComparison.Ordinal);
        Assert.Contains("AllowPaging=\"@(!Virtualize)\"", markup, StringComparison.Ordinal);
    }

    /// <summary>Radzen only virtualises inside a bounded, scrollable body. Without the height rule the
    /// virtualised grids would render as an unbounded, non-scrolling list.</summary>
    [Fact]
    public void TheVirtualizedGridHeight_IsDefinedInCss()
    {
        var css = File.ReadAllText(Path.Combine(FrontRoot, "wwwroot", "css", "app.css"));

        Assert.Contains(".aetheus-grid-virtualized", css, StringComparison.Ordinal);
        Assert.Contains(".aetheus-grid-virtualized .rz-data-grid-data", css, StringComparison.Ordinal);
    }
}
