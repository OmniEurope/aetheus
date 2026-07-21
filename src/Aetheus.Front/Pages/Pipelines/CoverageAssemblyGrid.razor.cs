// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

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
        var (sortBy, descending) = ResolveSort(args);
        _loading = true;
        _loadFailed = false;
        try
        {
            var result = await Api.GetCoverageAssembliesAsync(
                RunId, page, pageSize, sortBy: sortBy, sortDescending: descending);
            _assemblies = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private Task RetryAsync() => _grid?.Reload() ?? Task.CompletedTask;

    private static (string SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("LineRate", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatPercent(double rate) => rate.ToString("P1", CultureInfo.CurrentCulture);
}
