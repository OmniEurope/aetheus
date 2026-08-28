// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerProjectsSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }

    private List<ProjectDto> _projects = [];
    private int _totalCount;
    private bool _loading;
    private int? _loadedServerId;
    private int _loadGeneration;
    private Aetheus.Front.Shared.AetheusDataGrid<ProjectDto>? _grid;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        Interlocked.Increment(ref _loadGeneration);
        _projects = [];
        _totalCount = 0;
        if (_grid is not null) await _grid.Reload();
    }

    private async Task LoadProjectsAsync(LoadDataArgs args)
    {
        var serverId = ServerId;
        var generation = _loadGeneration;
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args);
        _loading = true;
        try
        {
            var result = await Api.Servers.GetServerProjectsPageAsync(serverId, page, pageSize, sortBy: sortBy, sortDescending: sortDescending);
            if (ServerId == serverId && generation == _loadGeneration)
            {
                _projects = result.Items;
                _totalCount = result.TotalCount;
            }
        }
        catch (HttpRequestException)
        {
            if (ServerId == serverId && generation == _loadGeneration)
            {
                _projects = [];
                _totalCount = 0;
            }
        } // 401 on expired JWT - redirect handled by AuthProvider
        finally
        {
            if (ServerId == serverId && generation == _loadGeneration)
                _loading = false;
        }
    }

    private static (string SortBy, bool SortDescending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Name", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private void OnRowClick(ProjectDto project)
    {
        Nav.NavigateTo($"/projects/{project.Id}/overview");
    }
}
