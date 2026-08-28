// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

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
    protected string? _search;
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
        ResetForProject();
        await LoadAsync();
    }

    protected async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_page, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = args.ToSortRequest(DefaultSortBy, DefaultSortDescending);
        await LoadAsync();
    }

    protected async Task OnSearchChangedAsync(object _)
    {
        if (_grid is not null && _page != 1)
        {
            _page = 1;
            await _grid.GoToPage(0);
            return;
        }

        _page = 1;
        await LoadAsync();
    }

    protected virtual void ResetForProject()
    {
    }

    protected abstract Task LoadAsync();
}
