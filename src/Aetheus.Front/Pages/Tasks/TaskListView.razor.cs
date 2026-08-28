// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Tasks;

/// <summary>
/// Reusable task list shared by the global <c>/tasks</c> page and the per-server tasks section.
/// When <see cref="ServerId"/> is set the grid is scoped to that one server (the redundant Server
/// column is also hidden); otherwise it shows every task the caller can see. Identical features in
/// both hosts: search, status filter, server-side pagination, status + offline badges, log viewer,
/// and live SignalR refresh.
/// </summary>
public partial class TaskListView : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    /// <summary>When set, scope the list to a single server (per-server tasks view).</summary>
    [Parameter] public int? ServerId { get; set; }
    [Parameter] public string? ServerName { get; set; }

    private AetheusDataGrid<ServerTaskDto>? _grid;
    private List<ServerTaskDto> _tasks = [];
    private int _totalCount;
    private bool _loading;
    private bool _loadFailed;
    private string? _search;
    private TaskExecutionStatus? _statusFilter;
    private HubConnection? _hubConnection;
    private bool _hasLoadedServerId;
    private int? _loadedServerId;

    private List<object> _statusOptions = [];

    protected override void OnInitialized()
    {
        _statusOptions =
        [
            new { Text = L.Localize(TaskExecutionStatus.Pending), Value = (TaskExecutionStatus?)TaskExecutionStatus.Pending },
            new { Text = L.Localize(TaskExecutionStatus.Running), Value = (TaskExecutionStatus?)TaskExecutionStatus.Running },
            new { Text = L.Localize(TaskExecutionStatus.Success), Value = (TaskExecutionStatus?)TaskExecutionStatus.Success },
            new { Text = L.Localize(TaskExecutionStatus.Failed), Value = (TaskExecutionStatus?)TaskExecutionStatus.Failed },
            new { Text = L.Localize(TaskExecutionStatus.Timeout), Value = (TaskExecutionStatus?)TaskExecutionStatus.Timeout },
            new { Text = L.Localize(TaskExecutionStatus.Cancelled), Value = (TaskExecutionStatus?)TaskExecutionStatus.Cancelled }
        ];
        // Stale-while-revalidate: pre-seed from the cached default view so revisiting the tasks list
        // paints instantly; OnLoadData then revalidates in the background.
        Cache.Seed<PaginatedResult<ServerTaskDto>>(CacheKey(1, 25, null, null), ApplyTasks);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_hasLoadedServerId)
        {
            _hasLoadedServerId = true;
            _loadedServerId = ServerId;
            return;
        }
        if (_loadedServerId == ServerId) return;

        _loadedServerId = ServerId;
        _tasks = [];
        _totalCount = 0;
        Cache.Seed<PaginatedResult<ServerTaskDto>>(CacheKey(1, 25, _search, _statusFilter), ApplyTasks);
        if (_grid is not null)
        {
            await _grid.GoToPage(0);
            await _grid.Reload();
        }
    }

    // The sort is part of the key: two orders sharing one entry means the second is served the first
    // one's rows, which is indistinguishable from a sort that does nothing.
    private string CacheKey(int page, int pageSize, string? search, TaskExecutionStatus? status,
        string? sortBy = null, bool sortDescending = true) =>
        $"tasks:{ServerId}:{page}:{pageSize}:{search}:{status}:{sortBy}:{sortDescending}";

    private void ApplyTasks(PaginatedResult<ServerTaskDto> result)
    {
        _tasks = result.Items;
        _totalCount = result.TotalCount;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            // Task lifecycle events ride the servers hub (AllServers group). Any of them refreshes
            // the grid so a new/started/finished task surfaces without a manual reload.
            _hubConnection = HubFactory.Create("servers");
            _hubConnection.On<object>("TaskQueued", _ => InvokeAsync(InvalidateAndReloadGridAsync));
            _hubConnection.On<object>("TaskStarted", _ => InvokeAsync(InvalidateAndReloadGridAsync));
            _hubConnection.On<object>("TaskCompleted", _ => InvokeAsync(InvalidateAndReloadGridAsync));
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinAllServers");
                await ReloadGridAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinAllServers");
        }
        catch { /* SignalR best-effort: fall back to manual refresh. */ }
    }

    private async Task ReloadGridAsync()
    {
        if (_grid is not null) await _grid.Reload();
    }

    private async Task InvalidateAndReloadGridAsync()
    {
        Cache.InvalidatePrefix("tasks:");
        await ReloadGridAsync();
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        _loadFailed = false;
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(ServerTaskDto.CreatedAt), fallbackDescending: true);
        var serverId = ServerId;
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, _search, _statusFilter, sortBy, sortDescending),
            () => Api.Pipelines.GetTasksAsync(page, pageSize, _search, _statusFilter, serverId, sortBy, sortDescending),
            result => { if (ServerId == serverId) ApplyTasks(result); },
            loading => { if (ServerId == serverId) _loading = loading; },
            () => InvokeAsync(StateHasChanged),
            _ => { if (ServerId == serverId) _loadFailed = true; });
    }

    private async Task ResetAndReload()
    {
        if (_grid is not null) await _grid.GoToPage(0);
    }

    private async Task ClearFilters()
    {
        _search = null;
        _statusFilter = null;
        await ResetAndReload();
    }

    internal void OpenTask(ServerTaskDto task) => Nav.NavigateTo($"/tasks/{task.Id}");

    // A Pending/Assigned task whose server agent is offline cannot progress on its own - surface that
    // instead of an opaque "Pending". The timeout watchdog force-fails it after 30 min.
    internal static bool IsStalledOnOfflineAgent(ServerTaskDto task) =>
        task.ServerStatus == ServerStatus.Offline
        && task.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned;

    internal string GetSourceServerName(ServerTaskDto task) =>
        !string.IsNullOrWhiteSpace(task.ServerName)
            ? task.ServerName
            : !string.IsNullOrWhiteSpace(ServerName)
                ? ServerName
                : $"#{task.ServerId}";

    internal static BadgeStyle GetTaskBadge(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Success => BadgeStyle.Success,
        TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout => BadgeStyle.Danger,
        TaskExecutionStatus.Running => BadgeStyle.Info,
        TaskExecutionStatus.Cancelled => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    internal static BadgeStyle GetFailureBadge(string failureCode) =>
        failureCode == TaskFailureCodes.ToolError ? BadgeStyle.Warning : BadgeStyle.Danger;

    internal static string GetFailureIcon(string failureCode) => failureCode switch
    {
        TaskFailureCodes.InfrastructureMismatch => "construction",
        TaskFailureCodes.TestsFailed => "science",
        TaskFailureCodes.BuildFailed => "build",
        _ => "report_problem"
    };

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeaveAllServers"); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}
