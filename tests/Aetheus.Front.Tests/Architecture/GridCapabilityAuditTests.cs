// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Every table in the product offers the same four affordances: sort, resize, advanced filtering and
/// scrolling through every row. A user who learns one table has learnt them all, and none of them
/// silently caps what it will show.
///
/// <para>The shared <c>AetheusDataGrid</c> carries all four by default, so a grid built on it needs no
/// declaration. A raw <c>OmniDataGrid</c> takes them from the default OmniEurope preset registered by
/// <c>AetheusGridPresets</c> (PLAN-012); this guard reads the markup so that no grid turns one off or
/// leaves the preset.</para>
///
/// <para>Recette R-327: no table shows a pager; every one scrolls. The last exception, the five-row
/// pager of <c>ReleasePickerGrid</c> (decision R-10), was lifted by the same recette: none is left.</para>
/// </summary>
public sealed class GridCapabilityAuditTests
{
    private static readonly string FrontRoot = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");

    /// <summary>The wrapper defines the defaults this guard enforces everywhere else, so it is checked
    /// by its own dedicated test below rather than by the sweep.</summary>
    private const string WrapperFile = "AetheusDataGrid.razor";

    /// <summary>Recette R-327: the grids that still page, each with its reason. Empty: every table
    /// scrolls. An entry needs the user's approval, like any guard exception.</summary>
    private static readonly Dictionary<string, string> PagedGrids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds the opening wrapper element (AetheusDataGridColumn does not exist, but the guard
    /// stays exact).</summary>
    private static readonly Regex WrapperOpen = new(
        @"<AetheusDataGrid(?![A-Za-z])",
        RegexOptions.Compiled);

    /// <summary>Finds the opening element only; OmniDataGridColumn is a different element.</summary>
    private static readonly Regex GridOpen = new(
        @"<OmniDataGrid(?!Column)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads one opening tag, ending it at the first '&gt;' that is NOT inside a quoted value.
    /// <para>A naive <c>[^&gt;]*&gt;</c> cuts the tag at the arrow of a lambda
    /// (<c>Click="@(() =&gt; Foo())"</c>) and then reports the attributes that follow as missing. That
    /// exact mistake produced a wave of false failures here, so the scan is character-based.</para>
    /// </summary>
    private static IReadOnlyList<string> TagsIn(string text) => TagsIn(text, GridOpen);

    private static IReadOnlyList<string> TagsIn(string text, Regex opening)
    {
        var tags = new List<string>();
        foreach (Match open in opening.Matches(text))
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
                throw new InvalidOperationException("Unterminated grid tag.");
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
                "No raw OmniDataGrid found. A guard that scans nothing passes vacuously.");
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
            .Where(grid => Declares(grid.Tag, "AllowSorting", "false"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table must be sortable. AllowSorting is disabled in:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRawGrid_AllowsColumnResize()
    {
        var offenders = RawGrids()
            .Where(grid => Declares(grid.Tag, "AllowColumnResize", "false"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table must let the user widen a truncated column. AllowColumnResize is disabled in:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRawGrid_UsesSimpleFiltering()
    {
        var offenders = RawGrids()
            .Where(grid => Declares(grid.Tag, "AllowFiltering", "false")
                           || DeclaresAny(grid.Tag, "FilterMode")
                              && !Declares(grid.Tag, "FilterMode", "OmniDataGridFilterMode.Simple"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table filters through the simple header menu, the affordance used across the "
            + "product. Filtering is disabled or uses another mode in:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// PLAN-012: the shared settings live in the default grid preset, so a raw grid that named another
    /// preset, or "none", would lose sorting and filtering without writing a single "false".
    /// </summary>
    [Fact]
    public void EveryRawGrid_KeepsTheDefaultPreset()
    {
        var offenders = RawGrids()
            .Where(grid => DeclaresAny(grid.Tag, "PresetName"))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every raw table takes the default grid preset (AetheusGridPresets). PresetName is set in:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// Recette R-327: every table scrolls instead of showing a pager. A raw grid declares its
    /// <c>ScrollMode</c> (OE merged AllowPaging and AllowVirtualization into it) without ever choosing
    /// <c>Paged</c>, and bounds its height (FillAvailableHeight when it ends the page, MaxHeight when it
    /// sits among other content or in a dialog), so it renders only its window. Deciding the mode from
    /// <c>AetheusGrid.Virtualization</c> is refused: that switch is off in production, so such a grid
    /// rendered every row in one tall block.
    /// </summary>
    [Fact]
    public void EveryRawGrid_ScrollsInsteadOfPaging()
    {
        var offenders = RawGrids()
            .Where(grid => !PagedGrids.ContainsKey(Path.GetFileName(grid.File)))
            .Where(grid => ScrollModeOf(grid.Tag) is not { } mode
                           || mode.Contains("OmniDataGridScrollMode.Paged", StringComparison.Ordinal)
                           || mode.Contains("AetheusGrid.Virtualization", StringComparison.Ordinal)
                           || !(DeclaresAny(grid.Tag, "FillAvailableHeight") || DeclaresAny(grid.Tag, "MaxHeight")))
            .Select(grid => grid.File)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every table scrolls instead of paging (recette R-327): ScrollMode declared and never Paged, "
            + "and a bounded height (FillAvailableHeight or MaxHeight). Missing in:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>The value bound to <c>ScrollMode</c>, a literal or an expression; null when undeclared.</summary>
    private static string? ScrollModeOf(string tag)
    {
        var match = Regex.Match(tag, @"(?<=[\s<])ScrollMode\s*=\s*""(?<value>@\(.*?\)|[^""]*)""", RegexOptions.Singleline);
        return match.Success ? match.Groups["value"].Value : null;
    }

    /// <summary>
    /// Recette R-327, the wrapper side: <c>AetheusDataGrid</c> pages unless it virtualizes, and its
    /// default follows <c>AetheusGrid.Virtualization</c>, off in production. So every caller declares how
    /// it scrolls: remote virtualization (<c>VirtualData</c> with <c>AetheusGrid.RemoteVirtualization</c>)
    /// for a server-paged list, or <c>Virtualize</c> for a fully loaded collection (always, or once it
    /// outgrows one page, which then never shows a pager). A <c>LoadData</c> grid without
    /// <c>VirtualData</c> pages whatever it declares, so it is refused too.
    /// </summary>
    [Fact]
    public void EveryWrapperGrid_ScrollsInsteadOfPaging()
    {
        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in RepositoryScan.Enumerate(FrontRoot, "*.razor"))
        {
            if (PagedGrids.ContainsKey(Path.GetFileName(file))) continue;
            foreach (var tag in TagsIn(File.ReadAllText(file), WrapperOpen))
            {
                scanned++;
                var remote = DeclaresAny(tag, "LoadData");
                var scrolls = remote
                    ? DeclaresAny(tag, "VirtualData") && Declares(tag, "Virtualize", "@AetheusGrid.RemoteVirtualization")
                    : DeclaresAny(tag, "Virtualize")
                      && !Declares(tag, "Virtualize", "false")
                      && !Declares(tag, "Virtualize", "@AetheusGrid.Virtualization");
                if (!scrolls) offenders.Add(Path.GetRelativePath(FrontRoot, file));
            }
        }

        Assert.True(scanned > 0, "No AetheusDataGrid found. A guard that scans nothing passes vacuously.");
        Assert.True(offenders.Count == 0,
            "Every table scrolls instead of paging (recette R-327). A remote list declares VirtualData and "
            + "Virtualize=\"@AetheusGrid.RemoteVirtualization\"; a local one declares Virtualize. Paged in:\n"
            + string.Join("\n", offenders.Distinct()));
    }

    /// <summary>Recette R-327: the release picker, the last paged table, scrolls through the virtual
    /// window like the other lists; its five-row pager (decision R-10) must not come back.</summary>
    [Fact]
    public void TheReleasePicker_Scrolls()
    {
        var picker = Path.Combine(FrontRoot, "Components", "Pipelines", "ReleasePickerGrid.razor");
        var tag = Assert.Single(TagsIn(File.ReadAllText(picker), WrapperOpen));

        Assert.True(Declares(tag, "Virtualize", "@AetheusGrid.RemoteVirtualization"), tag);
        Assert.True(DeclaresAny(tag, "VirtualData"), tag);
        Assert.Empty(PagedGrids);
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
            Path.Combine(FrontRoot, "Components", "Shared", "AetheusDataGrid.razor.cs"));

        Assert.Contains("public bool AllowSorting { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool AllowFiltering { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool AllowColumnResize { get; set; } = true;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public bool Virtualize { get; set; } = AetheusGrid.Virtualization;", wrapper, StringComparison.Ordinal);
        // The switch defaults to off: tables page. Turning it back on product-wide would restore the
        // bounded height, and with it the second scrollbar on every grid at once.
        Assert.Contains(
            "public static bool Virtualization { get; set; }",
            File.ReadAllText(Path.Combine(FrontRoot, "Components", "Shared", "AetheusGrid.cs")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static bool Virtualization { get; set; } = true;",
            File.ReadAllText(Path.Combine(FrontRoot, "Components", "Shared", "AetheusGrid.cs")),
            StringComparison.Ordinal);
        Assert.Contains("OmniDataGridFilterMode FilterMode { get; set; } = OmniDataGridFilterMode.Simple;", wrapper, StringComparison.Ordinal);

        var markup = File.ReadAllText(Path.Combine(FrontRoot, "Components", "Shared", "AetheusDataGrid.razor"));
        // Paging and virtualization are mutually exclusive: one ScrollMode chosen from the same flag is
        // what keeps the wrapper from silently rendering a pager over a virtualised body.
        Assert.Contains(
            "ScrollMode=\"@(EffectiveVirtualize ? OmniDataGridScrollMode.Virtual : OmniDataGridScrollMode.Paged)\"",
            markup, StringComparison.Ordinal);
        Assert.Contains("Load=\"@EffectiveLoad\"", markup, StringComparison.Ordinal);
        Assert.Contains("VirtualBlockSize=\"@PageSize\"", markup, StringComparison.Ordinal);

        Assert.Contains("public int PageSize { get; set; } = 20;", wrapper, StringComparison.Ordinal);
        Assert.Contains("public Func<OmniDataGridResult<TItem>>? VirtualData { get; set; }", wrapper, StringComparison.Ordinal);
    }

    /// <summary>The grid only virtualises inside a bounded, scrollable body. Without the height rule the
    /// virtualised grids would render as an unbounded, non-scrolling list.</summary>
    [Fact]
    public void TheVirtualizedGridHeight_IsDefinedInCss()
    {
        var css = File.ReadAllText(Path.Combine(FrontRoot, "wwwroot", "css", "app.css"));

        Assert.Contains(".aetheus-grid-virtualized", css, StringComparison.Ordinal);
        Assert.Contains(".aetheus-grid-virtualized .omni-data-grid__viewport", css, StringComparison.Ordinal);
    }
}
