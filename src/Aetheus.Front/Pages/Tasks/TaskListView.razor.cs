// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

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
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    /// <summary>When set, scope the list to a single server (per-server tasks view).</summary>
    [Parameter] public int? ServerId { get; set; }

    private AetheusDataGrid<ServerTaskDto>? _grid;
    private List<ServerTaskDto> _tasks = [];
    private int _totalCount;
    private bool _loading;
    private string? _search;
    private TaskExecutionStatus? _statusFilter;
    private List<TaskLogDto>? _selectedTaskLogs;
    internal int _selectedTaskId;
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
        _selectedTaskId = 0;
        _selectedTaskLogs = null;
        _tasks = [];
        _totalCount = 0;
        Cache.Seed<PaginatedResult<ServerTaskDto>>(CacheKey(1, 25, _search, _statusFilter), ApplyTasks);
        if (_grid is not null)
        {
            await _grid.GoToPage(0);
            await _grid.Reload();
        }
    }

    private string CacheKey(int page, int pageSize, string? search, TaskExecutionStatus? status) =>
        $"tasks:{ServerId}:{page}:{pageSize}:{search}:{status}";

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
            _hubConnection.On<object>("TaskQueued", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<object>("TaskStarted", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<object>("TaskCompleted", _ => InvokeAsync(ReloadGridAsync));
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

    private async Task OnLoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var serverId = ServerId;
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, _search, _statusFilter),
            () => Api.GetTasksAsync(page, pageSize, _search, _statusFilter, serverId),
            result => { if (ServerId == serverId) ApplyTasks(result); },
            loading => { if (ServerId == serverId) _loading = loading; },
            () => InvokeAsync(StateHasChanged));
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

    internal async Task ViewLogs(int taskId)
    {
        _selectedTaskId = taskId;
        _selectedTaskLogs = await Api.GetTaskLogsAsync(taskId);
        // Fire-and-forget the modal: awaiting OpenAsync would block until the dialog closes (and would
        // hang a bUnit render test). The logs are already fetched, so just hand them to the dialog body.
        _ = Dialog.OpenAsync<TaskLogsDialog>(
            string.Format(L["TaskLogsTitle"], taskId),
            new Dictionary<string, object?> { ["Logs"] = _selectedTaskLogs },
            new DialogOptions { Width = "min(900px, 92vw)", Height = "70vh", Resizable = true, Draggable = true });
    }

    // A Pending/Assigned task whose server agent is offline cannot progress on its own - surface that
    // instead of an opaque "Pending". The timeout watchdog force-fails it after 30 min.
    internal static bool IsStalledOnOfflineAgent(ServerTaskDto task) =>
        task.ServerStatus == ServerStatus.Offline
        && task.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned;

    internal static BadgeStyle GetTaskBadge(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Success => BadgeStyle.Success,
        TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout => BadgeStyle.Danger,
        TaskExecutionStatus.Running => BadgeStyle.Info,
        TaskExecutionStatus.Cancelled => BadgeStyle.Warning,
        _ => BadgeStyle.Light
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
