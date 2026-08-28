// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.VariableLibraries;

public partial class VersionHistoryDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    [Parameter] public int LibraryId { get; set; }
    [Parameter] public int EntryId { get; set; }

    private RadzenDataGrid<VariableEntryVersionDto>? _grid;
    private List<VariableEntryVersionDto> _versions = [];
    private int _totalCount;
    private bool _loading;
    private bool _loadFailed;

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var pageSize = args.Top ?? 25;
        var page = ((args.Skip ?? 0) / pageSize) + 1;
        var (sortBy, descending) = args.ToSortRequest("Version", true);
        await PaginatedGridLoader.LoadAsync(
            () => Api.Variables.GetVariableEntryVersionsPageAsync(
                LibraryId, EntryId, page, pageSize, sortBy: sortBy, sortDescending: descending),
            ApplyResult,
            loading => _loading = loading,
            failed => _loadFailed = failed);
    }

    private void ApplyResult(PaginatedResult<VariableEntryVersionDto> result) =>
        (_versions, _totalCount) = (result.Items, result.TotalCount);

    private Task RetryAsync() => PaginatedGridLoader.RetryAsync(_grid);

    private void Close() => Dialog.Close();
}
