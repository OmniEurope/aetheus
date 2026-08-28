// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Shared;

public partial class AetheusDataGrid<TItem> where TItem : notnull
{
    [Inject] private ILogger<AetheusDataGrid<TItem>> Logger { get; set; } = default!;

    [Parameter] public IEnumerable<TItem>? Data { get; set; }
    [Parameter] public int Count { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public EventCallback<LoadDataArgs> LoadData { get; set; }
    [Parameter] public RenderFragment? Columns { get; set; }
    [Parameter] public bool AllowSorting { get; set; } = true;
    [Parameter] public bool AllowFiltering { get; set; } = true;
    /// <summary>F-04: let users widen truncated columns. On by default for every shared grid.</summary>
    [Parameter] public bool AllowColumnResize { get; set; } = true;
    /// <summary>Advanced header filters are the common table affordance across the product. A caller
    /// can still opt into the simpler inline filter when that is genuinely clearer.</summary>
    [Parameter] public FilterMode FilterMode { get; set; } = FilterMode.Advanced;
    /// <summary>Rows per page. Kept at 25, the value the paged API calls already carry, so a page of UI
    /// maps to exactly one request.</summary>
    [Parameter] public int PageSize { get; set; } = 25;

    /// <summary>Infinite scroll instead of a pager. OFF by default since 2026-08-23.
    /// <para>Radzen can only virtualise inside a bounded height, so the grid grew its own scroll body
    /// while the page kept its own: two scrollbars on the same table, which is what a reader actually
    /// hits. Paging and virtualization are mutually exclusive in Radzen, so the pager is the fix, not a
    /// workaround. A caller that genuinely wants infinite scroll sets <c>Virtualize="true"</c> and
    /// takes the bounded height with it.</para></summary>
    [Parameter] public bool Virtualize { get; set; } = AetheusGrid.Virtualization;

    /// <summary>Rows rendered above and below the viewport. Three is enough to hide the fetch during a
    /// normal scroll without inflating the DOM.</summary>
    [Parameter] public int VirtualizationOverscanCount { get; set; } = 3;
    [Parameter] public Density Density { get; set; } = Density.Compact;
    [Parameter] public string? EmptyText { get; set; }
    [Parameter] public RenderFragment? EmptyTemplate { get; set; }
    [Parameter] public bool AutoLoad { get; set; } = true;
    /// <summary>E-ter: smart row click. When set, the whole row becomes the primary link
    /// (cursor + hover) and a click anywhere on the row invokes this with the row item.
    /// Secondary links/buttons inside cells must use <c>@onclick:stopPropagation</c>.</summary>
    [Parameter] public EventCallback<TItem> RowClick { get; set; }
    /// <summary>E: when set, the grid fills the viewport height and scrolls its body internally
    /// (the pager stays pinned at the bottom). Use on full-page single-grid views.</summary>
    [Parameter] public bool FullHeight { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    public RadzenDataGrid<TItem>? Grid { get; private set; }

    /// <summary>Merges the caller's <c>class</c> with <c>aetheus-clickable-rows</c> when a
    /// <see cref="RowClick"/> handler is wired (so cursor:pointer only shows on clickable rows).</summary>
    private string GridCssClass
    {
        get
        {
            var passed = AdditionalAttributes is not null
                && AdditionalAttributes.TryGetValue("class", out var c) ? c?.ToString() : null;
            var classes = new List<string>();
            if (!string.IsNullOrWhiteSpace(passed)) classes.Add(passed);
            // Every grid built on this wrapper, not just the virtualised ones. The ten-row floor used
            // to hang off aetheus-grid-virtualized, so a plain grid (the runs table on a pipeline
            // page, for one) got no floor at all and collapsed onto its own scrollbar.
            classes.Add("aetheus-grid");
            if (RowClick.HasDelegate) classes.Add("aetheus-clickable-rows");
            if (FullHeight) classes.Add("aetheus-grid-fullheight");
            // Always applied when virtualising, FullHeight included. Virtualization is circular without
            // it: Radzen renders rows to fill the scroll body, but the body only has a height once rows
            // exist, so a body left at min-height 0 stays 0 and the grid renders empty. FullHeight caps
            // the height; this floor is what stops it collapsing.
            if (Virtualize) classes.Add("aetheus-grid-virtualized");
            return string.Join(" ", classes);
        }
    }

    /// <summary>All splatted attributes except <c>class</c> (rendered explicitly via <see cref="GridCssClass"/>).</summary>
    private Dictionary<string, object>? OtherAttributes =>
        AdditionalAttributes?.Where(kv => kv.Key != "class").ToDictionary(kv => kv.Key, kv => kv.Value);

    private async Task OnRowClick(DataGridRowMouseEventArgs<TItem> args)
    {
        if (RowClick.HasDelegate)
            await RowClick.InvokeAsync(args.Data);
    }

    // W3PN: surface a load failure instead of leaving the grid stuck on its perpetual refresh spinner.
    private bool _loadFailed;

    /// <summary>Wraps the caller's <see cref="LoadData"/> so a failed fetch (the caller didn't reset its
    /// loading flag, or threw) flips the grid into an explicit error state with a Retry rather than an
    /// endless spinner. Centralised here so every grid built on this wrapper inherits the behaviour.</summary>
    private async Task OnLoadData(LoadDataArgs args)
    {
        if (!LoadData.HasDelegate) return;
        try
        {
            _loadFailed = false;
            await LoadData.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            // W3PN: a swallowed load failure must still be diagnosable; without this the grid drops
            // into its error state with no trace of the underlying null-ref / 500 / bug.
            Logger.LogWarning(ex, "Grid LoadData failed for {ItemType}; showing the error state.", typeof(TItem).Name);
            _loadFailed = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RetryAsync()
    {
        _loadFailed = false;
        await Reload();
    }

    private bool _autoLoadTriggered;

    // Radzen.Blazor 10.x does NOT auto-fire LoadData when the grid is wrapped in a
    // generic component while `Data` is pre-bound to a non-null empty list. Trigger
    // an explicit Reload() on first render to populate the grid; subsequent reloads
    // (filter/sort/paging changes) are still driven by RadzenDataGrid itself.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && AutoLoad && !_autoLoadTriggered && LoadData.HasDelegate && Grid is not null)
        {
            _autoLoadTriggered = true;
            await Grid.Reload();
        }
    }

    public async Task Reload() => await (Grid?.Reload() ?? Task.CompletedTask);

    public async Task GoToPage(int page) => await (Grid?.GoToPage(page) ?? Task.CompletedTask);
}
