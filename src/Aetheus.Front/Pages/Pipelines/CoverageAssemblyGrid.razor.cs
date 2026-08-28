// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Pages.Pipelines;

public partial class CoverageAssemblyGrid
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired]
    public int RunId { get; set; }

    private RadzenDataGrid<CoverageAssemblyDto>? _grid;
    private List<CoverageAssemblyDto> _assemblies = [];
    private int _totalCount;
    private bool _loading;
    private bool _loadFailed;

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var pageSize = args.Top ?? 12;
        var page = ((args.Skip ?? 0) / pageSize) + 1;
        var (sortBy, descending) = args.ToSortRequest("LineRate");
        await PaginatedGridLoader.LoadAsync(
            () => Api.Packages.GetCoverageAssembliesAsync(
                RunId, page, pageSize, sortBy: sortBy, sortDescending: descending),
            ApplyResult,
            loading => _loading = loading,
            failed => _loadFailed = failed);
    }

    private void ApplyResult(PaginatedResult<CoverageAssemblyDto> result) =>
        (_assemblies, _totalCount) = (result.Items, result.TotalCount);

    private Task RetryAsync() => PaginatedGridLoader.RetryAsync(_grid);

    private static string FormatPercent(double rate) => rate.ToString("P1", CultureInfo.CurrentCulture);
}
