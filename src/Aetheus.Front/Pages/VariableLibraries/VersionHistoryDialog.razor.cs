// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

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
        var (sortBy, descending) = ResolveSort(args);
        _loading = true;
        _loadFailed = false;
        try
        {
            var result = await Api.GetVariableEntryVersionsPageAsync(
                LibraryId, EntryId, page, pageSize, sortBy: sortBy, sortDescending: descending);
            _versions = result.Items;
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
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Version", true);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private static BadgeStyle GetBadge(ChangeType type) => type switch
    {
        ChangeType.Created => BadgeStyle.Success,
        ChangeType.Updated => BadgeStyle.Info,
        ChangeType.Deleted => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private void Close() => Dialog.Close();
}
