// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class ReleasePickerGrid
{
    /// <summary>R-10 put every release in one place (no more "Show more", "All releases", "Show more");
    /// recette R-327 replaced its five-row pager with scrolling: this is the size of the block the grid
    /// asks for as the reader scrolls.</summary>
    internal const int PageSize = 20;

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<ReleaseDto> Releases { get; set; } = [];

    /// <summary>How many releases the project can restore in all; above <see cref="Releases"/>'s count
    /// the grid says it shows only the most recent ones rather than letting the rest vanish.</summary>
    [Parameter] public int TotalCount { get; set; }

    /// <summary>Raised with the full version of the clicked row; unset when no field can take it.</summary>
    [Parameter] public EventCallback<string> Pick { get; set; }

    private List<ReleaseDto> _page = [];
    private int _count;

    // Recette R-210: the two status columns filter by a checkable list of their members.
    private Func<string, string>? _statusText;
    private Func<string, string> StatusText => _statusText ??= GridFilterText.ForEnum<ReleaseStatus>(L);
    private Func<string, string>? _runStatusText;
    private Func<string, string> RunStatusText => _runStatusText ??= GridFilterText.ForEnum<PipelineStatus>(L);

    /// <summary>The grid's window (or, in a host without virtualization, its page) is cut from the loaded
    /// list here, after its sort and filters (a wrapper grid always asks through LoadData), so the header
    /// affordances act on every release, not on the rows on screen.</summary>
    private void LoadPage(GridLoadArgs args)
    {
        _page = args.ToClientPage(Releases, PageSize);
        _count = args.ClientFilteredCount(Releases);
    }
}
