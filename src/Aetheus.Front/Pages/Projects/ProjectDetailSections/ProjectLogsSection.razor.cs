// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectLogsSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public int ProjectId { get; set; }

    private PaginatedResult<TaskLogDto>? _result;
    private int? _loadedProjectId;
    private int _page = 1;
    private int _pageSize = 25;
    private string _sortBy = "Timestamp";
    private bool _sortDescending = true;
    private string? _search;
    private AetheusDataGrid<TaskLogDto>? _grid;
    private bool _loading;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedProjectId == ProjectId) return;
        _loadedProjectId = ProjectId;
        _page = 1;
        await LoadAsync();
    }

    private async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_page, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = GetSort(args, "Timestamp", true);
        await LoadAsync();
    }

    private async Task OnSearchChangedAsync(object _)
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

    private async Task LoadAsync()
    {
        var projectId = ProjectId;
        _loading = true;
        PaginatedResult<TaskLogDto> result;
        try
        {
            result = await Api.GetProjectLogsAsync(
            projectId, _page, _pageSize, _search, _sortBy, _sortDescending);
        }
        catch (HttpRequestException) { result = new PaginatedResult<TaskLogDto>(); }
        if (ProjectId != projectId) return;
        _result = result;
        _loading = false;
    }

    private static (string SortBy, bool Descending) GetSort(
        LoadDataArgs args, string fallback, bool fallbackDescending)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (fallback, fallbackDescending);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }

    private static BadgeStyle GetLevelBadge(TaskLogLevel level) => level switch
    {
        TaskLogLevel.Error => BadgeStyle.Danger,
        TaskLogLevel.Warning => BadgeStyle.Warning,
        TaskLogLevel.Info => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };
}
