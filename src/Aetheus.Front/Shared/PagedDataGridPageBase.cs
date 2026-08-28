// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public abstract class PagedDataGridPageBase<T> : ComponentBase
    where T : notnull
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected DialogService Dialog { get; set; } = default!;
    [Inject] protected BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] protected UiActions Ui { get; set; } = default!;

    protected RadzenDataGrid<T>? _grid;
    protected int _totalCount;
    protected bool _loading;

    protected async Task ReloadPageAsync()
    {
        if (_grid is not null)
        {
            await _grid.GoToPage(0, forceReload: true).ConfigureAwait(false);
            return;
        }

        await LoadPageAsync(1, 25).ConfigureAwait(false);
    }

    protected Task LoadPageFromArgsAsync(LoadDataArgs args)
    {
        var pageSize = args.Top ?? 25;
        var page = (args.Skip ?? 0) / pageSize + 1;
        return LoadPageAsync(page, pageSize);
    }

    protected async Task<PaginatedResult<T>> LoadPageResultAsync(
        Func<Task<PaginatedResult<T>>> loadAsync)
    {
        _loading = true;
        try
        {
            var result = await loadAsync().ConfigureAwait(false);
            _totalCount = result.TotalCount;
            return result;
        }
        finally
        {
            _loading = false;
        }
    }

    protected abstract Task LoadPageAsync(int page, int pageSize);
}
