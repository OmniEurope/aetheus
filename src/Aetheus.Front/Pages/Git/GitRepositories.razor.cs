// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Pages.Git;

public partial class GitRepositories : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ILogger<GitRepositories> Logger { get; set; } = default!;

    private HubConnection? _hub;
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private AetheusDataGrid<GitLightRepoDto>? _grid;
    private readonly HashSet<int> _joinedRepositoryIds = [];

    [SupplyParameterFromQuery(Name = "projectId")]
    public int? ProjectId { get; set; }

    [SupplyParameterFromQuery(Name = "create")]
    public bool CreateRequested { get; set; }

    private List<GitLightRepoDto> _repos = [];
    private int _totalCount;
    private List<ProjectDto> _projects = [];
    private bool _projectsLoaded;
    private string? _search;
    private bool _loading;
    private bool _canWrite;
    private int? _projectFilter;
    private int? _lastAppliedProjectId;
    private int _currentPage = 1;
    private int _pageSize = 25;
    private string _sortBy = "Name";
    private bool _sortDescending;
    private bool _createDialogOpened;
    internal bool IsLoading => _loading;
    internal int? ActiveProjectFilter => _projectFilter;
    internal IReadOnlyList<GitLightRepoDto> Repositories => _repos;
    internal int RepositoryCount => _totalCount;
    private string GitGridClass => !_loading && _totalCount == 0
        ? "pipeline-list-grid git-repositories-grid-empty"
        : "pipeline-list-grid";

    protected override async Task OnInitializedAsync()
    {
        Breadcrumb.Set(new BreadcrumbItem(L["GitRepositories"]));
        // Subscribe BEFORE reading: MainLayout loads permissions in parallel with
        // route activation, so the page can mount before they're ready. Without
        // the subscription, _canWrite would stay false until the next navigation
        // - stranding "New Repository" as permanently disabled.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        try
        {
            _projects = await Api.Projects.GetAllProjectsAsync();
            _projectsLoaded = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            GitLoadFailureLogger.Log(Logger, ex, "projects", detailPage: false);
            _projects = [];
            _projectsLoaded = false;
        }
        ApplyProjectFilterFromQuery();
        await LoadData();
        if (RedirectToOnlyRepositoryIfNeeded()) return;
        await ConnectHub();
    }

    private async Task ConnectHub()
    {
        try
        {
            _hub = HubFactory.Create("git");
            _hub.On<int>("RepositoryChanged", _ => InvokeAsync(async () => { await LoadData(); StateHasChanged(); }));
            _hub.On<int>("CommitsChanged", _ => InvokeAsync(async () => { await LoadData(); StateHasChanged(); }));
            _hub.On<int>("BranchesChanged", _ => InvokeAsync(async () => { await LoadData(); StateHasChanged(); }));
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hub.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                _joinedRepositoryIds.Clear();
                await LoadData();
                StateHasChanged();
            }));
            await _hub.StartAsync();
            await JoinVisibleRepositoryGroupsAsync();
        }
        // Hub unavailable (server down/restarting, negotiation rejected, or navigation
        // cancelled the connect) - the page works without real-time updates. Silent by
        // design: pages have no ILogger by convention.
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    private async Task JoinVisibleRepositoryGroupsAsync()
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        var visible = _repos.Select(repository => repository.Id).ToHashSet();
        foreach (var repositoryId in _joinedRepositoryIds.Except(visible).ToList())
        {
            await _hub.InvokeAsync("LeaveRepositoryGroup", repositoryId);
            _joinedRepositoryIds.Remove(repositoryId);
        }
        foreach (var repositoryId in visible.Except(_joinedRepositoryIds))
        {
            await _hub.InvokeAsync("JoinRepositoryGroup", repositoryId);
            _joinedRepositoryIds.Add(repositoryId);
        }
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_createDialogOpened || !CreateRequested || !_projectFilter.HasValue || !_canWrite) return;
        _createDialogOpened = true;
        await ShowCreateDialog();
        Nav.NavigateTo($"git-repositories?projectId={_projectFilter.Value}", replace: true);
    }

    private void RefreshCanWrite() =>
        _canWrite = Permissions.CanWrite(Aetheus.Shared.Enums.ResourceType.Project);

    protected override async Task OnParametersSetAsync()
    {
        if (_lastAppliedProjectId == ProjectId)
        {
            return;
        }

        _lastAppliedProjectId = ProjectId;
        ApplyProjectFilterFromQuery();
        await ResetAndReloadAsync();
    }

    internal async Task LoadData()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        using var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        _loading = true;
        // No project filter => list every repo the user can access (backend cross-project view);
        // a filter narrows to that project. try/catch for the OnInitializedAsync path: an expired-JWT
        // 401 must fall through to an empty list, not trip the ErrorBoundary during the auth redirect.
        try
        {
            var result = await Api.Git.GetGitReposPageAsync(
                _currentPage, _pageSize, _projectFilter, _search, _sortBy, _sortDescending, cts.Token);
            if (generation != _loadGeneration) return;
            _repos = result.Items;
            _totalCount = result.TotalCount;
            await JoinVisibleRepositoryGroupsAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            if (generation != _loadGeneration) return;
            GitLoadFailureLogger.Log(Logger, ex, "repositories", detailPage: false);
            _repos = [];
            _totalCount = 0;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _loading = false;
                Interlocked.CompareExchange(ref _loadCts, null, cts);
            }
        }
    }

    internal async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_currentPage, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = args.ToSortRequest("Name");
        await LoadData();
    }

    private async Task ResetAndReloadAsync()
    {
        if (_grid is not null && _currentPage != 1)
        {
            _currentPage = 1;
            await _grid.GoToPage(0);
            return;
        }

        _currentPage = 1;
        await LoadData();
    }

    private async Task ShowCreateDialog()
    {
        if (!_projectFilter.HasValue) return;

        var name = await Dialog.OpenAsync<GitRepositoryCreateDialog>(
            L["NewRepository"].Value,
            new Dictionary<string, object?> { { "ProjectId", _projectFilter.Value } },
            new DialogOptions { Width = "500px", AutoFocusFirstElement = false });

        if (name is true)
        {
            await ResetAndReloadAsync();
        }
    }

    private async Task DeleteRepo(GitLightRepoDto repo)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteConfirm"].Value, repo.Name),
            L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var result = await Api.Git.DeleteGitRepoAsync(repo.Id);
        if (result)
        {
            Toast.Success("Deleted", repo.Name);
            await ResetAndReloadAsync();
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    private async Task ClearFilters()
    {
        _projectFilter = null;
        SyncUrlWithCurrentFilter();
        await ResetAndReloadAsync();
    }

    private async Task OnProjectFilterChanged(object _)
    {
        SyncUrlWithCurrentFilter();
        await ResetAndReloadAsync();
    }

    private void ApplyProjectFilterFromQuery()
    {
        if (!ProjectId.HasValue)
        {
            _projectFilter = null;
            return;
        }

        _projectFilter = !_projectsLoaded || _projects.Any(p => p.Id == ProjectId.Value)
            ? ProjectId.Value
            : null;
    }

    private void SyncUrlWithCurrentFilter()
    {
        var target = _projectFilter.HasValue
            ? $"git-repositories?projectId={_projectFilter.Value}"
            : "git-repositories";

        var current = Nav.ToBaseRelativePath(Nav.Uri);
        if (!string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo(target, replace: true);
        }
    }

    private bool RedirectToOnlyRepositoryIfNeeded()
    {
        if (CreateRequested || !_projectFilter.HasValue || !string.IsNullOrWhiteSpace(_search)
            || HasExternalRepositoryFragment() || _currentPage != 1 || _totalCount != 1 || _repos.Count != 1)
            return false;

        var repository = _repos[0];
        Nav.NavigateTo($"git-repositories/{repository.Id}?projectId={repository.ProjectId}", replace: true);
        return true;
    }

    private bool HasExternalRepositoryFragment() => string.Equals(
        new Uri(Nav.Uri).Fragment,
        "#external-repository",
        StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _loadGeneration);
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hub is not null)
        {
            // SignalR removes every repository-group membership when the connection closes.
            // Do not invoke the removed global-group contract during teardown: disposal errors
            // escape the component lifecycle and trigger Blazor's global error boundary.
            try { await _hub.DisposeAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException) { }
        }
        _joinedRepositoryIds.Clear();
    }
}
