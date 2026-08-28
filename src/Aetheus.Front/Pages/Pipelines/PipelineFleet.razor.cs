// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineFleet : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;

    private RadzenDataGrid<PipelineFleetItemDto>? _grid;
    private List<PipelineFleetItemDto> _items = [];
    private List<PipelineTemplateSummaryDto> _templates = [];
    private List<ProjectDto> _projects = [];
    private List<object> _freshnessOptions = [];
    private int _totalCount;
    private bool _loading;
    private bool _filtersLoadError;
    private bool _gridLoadError;
    private string? _search;
    private int? _templateId;
    private int? _projectId;
    private PipelineFleetFreshness? _freshness;
    private int _page = 1;
    private int _pageSize = 25;
    private string? _sortBy;
    private bool _sortDescending;
    private readonly object _permissionRefreshLock = new();
    private Task _permissionRefreshTask = Task.CompletedTask;

    protected override async Task OnInitializedAsync()
    {
        Breadcrumb.Set(new BreadcrumbItem(L["PipelineFleet"]));
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        _freshnessOptions =
        [
            new { Text = L["Current"].Value, Value = (PipelineFleetFreshness?)PipelineFleetFreshness.Current },
            new { Text = L["Outdated"].Value, Value = (PipelineFleetFreshness?)PipelineFleetFreshness.Outdated },
            new { Text = L["OffCatalog"].Value, Value = (PipelineFleetFreshness?)PipelineFleetFreshness.OffCatalog }
        ];
        try
        {
            var templatesTask = Api.PipelineTemplates.GetPipelineTemplatesAsync();
            var projectsTask = Api.Projects.GetAllProjectsAsync();
            await Task.WhenAll(templatesTask, projectsTask);
            _templates = await templatesTask;
            _projects = await projectsTask;
        }
        catch (HttpRequestException)
        {
            _filtersLoadError = true;
        }
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        _pageSize = args.Top ?? 25;
        _page = ((args.Skip ?? 0) / _pageSize) + 1;
        (_sortBy, _sortDescending) = ResolveSort(args);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _gridLoadError = false;
        try
        {
            var result = await Api.Pipelines.GetPipelineFleetAsync(
                _page, _pageSize, _search, _templateId, _projectId, _freshness,
                _sortBy, _sortDescending);
            _items = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _items = [];
            _totalCount = 0;
            _gridLoadError = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private static (string? SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (null, false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var allowed = parts[0] is "PipelineName" or "OwnerName" or "TemplateName"
            or "PinnedVersion" or "LatestVersion" or "Freshness";
        return allowed
            ? (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase))
            : (null, false);
    }

    private Task OnSearchChanged(string? value)
    {
        _search = value;
        return ReloadAsync();
    }

    private Task OnTemplateChanged(int? value)
    {
        _templateId = value;
        return ReloadAsync();
    }

    private Task OnProjectChanged(int? value)
    {
        _projectId = value;
        return ReloadAsync();
    }

    private Task OnFreshnessChanged(PipelineFleetFreshness? value)
    {
        _freshness = value;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _page = 1;
        if (_grid is not null && _grid.CurrentPage != 0)
            await _grid.GoToPage(0);
        else
            await LoadAsync();
    }

    private void OpenUpdateAsync(PipelineFleetItemDto item) =>
        Nav.NavigateTo($"/pipelines/{item.PipelineId}/template/update/{item.LatestVersion!.Value}");

    private bool CanUpdate(PipelineFleetItemDto item) =>
        Permissions.CanWrite(ResourceType.Pipeline, item.PipelineId);

    private string FreshnessText(PipelineFleetFreshness freshness) => freshness switch
    {
        PipelineFleetFreshness.Current => L["Current"],
        PipelineFleetFreshness.Outdated => L["Outdated"],
        _ => L["OffCatalog"]
    };

    private static BadgeStyle FreshnessStyle(PipelineFleetFreshness freshness) => freshness switch
    {
        PipelineFleetFreshness.Current => BadgeStyle.Success,
        PipelineFleetFreshness.Outdated => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    private void OnPermissionsChanged()
    {
        lock (_permissionRefreshLock)
            _permissionRefreshTask = RefreshPermissionsAsync(_permissionRefreshTask);
    }

    private async Task RefreshPermissionsAsync(Task previousRefresh)
    {
        await previousRefresh;
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        Task pendingRefresh;
        lock (_permissionRefreshLock)
            pendingRefresh = _permissionRefreshTask;
        await pendingRefresh;
    }
}
