// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.ProjectDetailSections;

public abstract class ProjectPagedSectionBase<TItem> : ComponentBase
    where TItem : notnull
{
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected ApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public int ProjectId { get; set; }

    protected int? _loadedProjectId;
    protected int _page = 1;
    protected int _pageSize = 25;
    protected string _sortBy = string.Empty;
    protected bool _sortDescending;
    /// <summary>
    /// Recette R-183 / R-184 / R-212: no search box above the grid; each header filter is a real column
    /// filter the section's endpoint applies (the former "first typed value becomes the search term"
    /// fallback is gone, every section now sends column filters).
    /// </summary>
    protected List<Aetheus.Shared.Components.Shared.GridFilter> _columnFilters = [];
    protected AetheusDataGrid<TItem>? _grid;
    protected bool _loading;

    protected abstract string DefaultSortBy { get; }
    protected virtual bool DefaultSortDescending => false;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedProjectId == ProjectId) return;
        _loadedProjectId = ProjectId;
        _page = 1;
        _sortBy = DefaultSortBy;
        _sortDescending = DefaultSortDescending;
        _columnFilters = [];
        ResetForProject();
        await LoadAsync();
    }

    protected async Task OnLoadDataAsync(GridLoadArgs args)
    {
        (_page, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = args.ToSortRequest(DefaultSortBy, DefaultSortDescending);
        _columnFilters = args.ToApiFilters();
        await LoadAsync();
    }

    protected virtual void ResetForProject()
    {
    }

    protected abstract Task LoadAsync();
}
