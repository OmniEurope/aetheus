// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Servers;

public partial class Servers : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TooltipService TooltipService { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    private AetheusDataGrid<ServerDto>? _grid;
    private List<ServerDto> _servers = [];
    private int _totalCount;
    private bool _loading;
    internal bool _canWrite;
    internal string? _search;
    internal ServerType? _typeFilter;
    internal ServerStatus? _statusFilter;
    private List<object> _typeOptions = [];
    private List<object> _statusOptions = [];
    private HubConnection? _hubConnection;

    // --- Column chooser (S-FEAT-10): per-user column visibility persisted to localStorage ---
    private const string ColumnVisibilityStorageKey = "servers-columns-hidden";
    private bool _showColumnChooser;
    // Toggleable columns (Name/Status/Actions always visible). Default = all shown.
    private readonly Dictionary<string, bool> _columnVisibility = new()
    {
        ["Type"] = true,
        ["Hostname"] = true,
        ["OsDescription"] = true,
        ["AgentVersion"] = true,
        ["Tags"] = true,
        ["LastHeartbeat"] = true,
    };

    // Column key → localized label, for the chooser checkboxes.
    private IEnumerable<KeyValuePair<string, string>> _columnLabels =>
    [
        new("Type", L["Type"]),
        new("Hostname", L["Hostname"]),
        new("OsDescription", L["OS"]),
        new("AgentVersion", L["AgentVersion"]),
        new("Tags", L["Tags"]),
        new("LastHeartbeat", L["LastHeartbeat"]),
    ];

    internal bool IsColumnVisible(string key) =>
        !_columnVisibility.TryGetValue(key, out var visible) || visible;

    private void ToggleColumnChooser() => _showColumnChooser = !_showColumnChooser;

    private async Task OnColumnVisibilityChanged(string key, bool visible)
    {
        _columnVisibility[key] = visible;
        await PersistColumnVisibilityAsync();
    }

    private async Task PersistColumnVisibilityAsync()
    {
        // Store only the hidden columns (compact); absence => visible.
        var hidden = _columnVisibility.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        await JS.InvokeVoidAsync("Aetheus.setLocal", ColumnVisibilityStorageKey, string.Join(",", hidden));
    }

    private async Task LoadColumnVisibilityAsync()
    {
        var raw = await JS.InvokeAsync<string?>("Aetheus.getLocal", ColumnVisibilityStorageKey);
        if (string.IsNullOrWhiteSpace(raw)) return;
        var hidden = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var key in _columnVisibility.Keys.ToList())
            _columnVisibility[key] = !hidden.Contains(key);
    }

    // Why a server is unreachable, surfaced client-side from the list payload
    // so operators don't need the Contact dialog. Shared with ServerDetail.
    private string OfflineReason(ServerDto server) =>
        ServerHeartbeatHelper.OfflineReason(L, server.LastHeartbeat);

    // Stale-while-revalidate cache key for one grid view (page + size + sort + every filter). The
    // default view (page 1 / size 25 / no filter / no sort) is what landing on /servers hits, so a
    // return visit renders the previous page instantly while the live fetch revalidates in the
    // background. SignalR patches keep the rendered rows fresh, so the cache only smooths the paint.
    private string CacheKey(int page, int pageSize, string? sortBy, bool sortDescending) =>
        $"servers:{page}:{pageSize}:{_search}:{_typeFilter}:{_statusFilter}:{sortBy}:{sortDescending}";

    protected override async Task OnInitializedAsync()
    {
        _typeOptions =
        [
            new { Text = L.Localize(ServerType.Normal), Value = (ServerType?)ServerType.Normal },
            new { Text = L.Localize(ServerType.Build), Value = (ServerType?)ServerType.Build },
            new { Text = L.Localize(ServerType.Docker), Value = (ServerType?)ServerType.Docker }
        ];
        _statusOptions =
        [
            new { Text = L.Localize(ServerStatus.Online), Value = (ServerStatus?)ServerStatus.Online },
            new { Text = L.Localize(ServerStatus.Offline), Value = (ServerStatus?)ServerStatus.Offline }
        ];
        Breadcrumb.Set(new BreadcrumbItem(L["Servers"]));
        // Subscribe BEFORE reading: MainLayout loads permissions in parallel with
        // route activation, so the page can mount before they're ready. Without
        // the subscription, _canWrite would stay false until the next navigation
        // - stranding "Add Agent" / "Update All Agents" as permanently disabled.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        // LoadData auto-fired by RadzenDataGrid.

        // Pre-seed the grid from the cached default view so the first paint shows the previous rows
        // instead of an empty spinner; OnLoadData then revalidates in the background (stale-while-revalidate).
        Cache.Seed<PaginatedResult<ServerDto>>(CacheKey(1, LoadDataArgsExtensions.DefaultPageSize, null, false), ApplyServers);

        await LoadColumnVisibilityAsync();
        await StartHubAsync();
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() =>
        _canWrite = Permissions.CanWrite(Aetheus.Shared.Enums.ResourceType.Server);

    private async Task StartHubAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("servers");
            // Lifecycle events (register/update/remove/offline) reload the whole grid - rare. Heartbeats
            // (~every 30s PER server) are coalesced into a single trailing reload (see OnHeartbeatAsync)
            // so a large fleet's clustered heartbeats can't thrash the grid.
            _hubConnection.On<ServerDto>("ServerRegistered", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<ServerDto>("ServerUpdated", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<int>("ServerRemoved", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<int>("ServerOffline", _ => InvokeAsync(ReloadGridAsync));
            _hubConnection.On<int, ServerHeartbeatDto>("ServerHeartbeat", (id, heartbeat) => InvokeAsync(() => OnHeartbeatAsync(id, heartbeat)));
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload to
            // catch broadcasts missed while disconnected (e.g. a server enrolled during a blip).
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinAllServers");
                await ReloadGridAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinAllServers");
        }
        catch
        {
            // SignalR is best-effort: fall back to manual refresh.
        }
    }

    private async Task ReloadGridAsync()
    {
        if (_grid is not null) await _grid.Reload();
    }

    private const int HeartbeatDebounceMs = 5000;
    private readonly TrailingReloadCoalescer _heartbeatCoalescer = new(HeartbeatDebounceMs);

    // S-TECH-T3M8: a heartbeat only changes a server's live fields (Status / LastHeartbeat /
    // AgentVersion / AgentInstalledAt / DockerAvailable / InsecureTls). When it targets a server
    // already on the current page AND no status filter is active (so the patched Status can't move
    // the row in or out of the page), we replace that one row via the record's `with` - ServerDto
    // is init-only but freely copyable - and re-render just the grid: no LoadData round-trip, no
    // flicker, off-page heartbeats ignored outright. The full-reload path (still coalesced into a
    // single trailing reload per burst) is kept only where a targeted patch can't stay consistent:
    // an active status filter, where the new Online status may change page membership.
    private Task OnHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat)
    {
        if (_statusFilter is not null)
            return _heartbeatCoalescer.RequestAsync(ReloadGridAsync);

        var index = _servers.FindIndex(s => s.Id == serverId);
        if (index < 0)
            return Task.CompletedTask; // not on the current page → nothing visible changes

        _servers[index] = _servers[index] with
        {
            Status = ServerStatus.Online,
            LastHeartbeat = DateTime.Now,
            AgentVersion = heartbeat.AgentVersion ?? _servers[index].AgentVersion,
            AgentInstalledAt = heartbeat.AgentInstalledAt ?? _servers[index].AgentInstalledAt,
            DockerAvailable = heartbeat.DockerAvailable,
            InsecureTls = heartbeat.InsecureTls
        };
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeaveAllServers"); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    private void ApplyServers(PaginatedResult<ServerDto> result)
    {
        _servers = result.Items;
        _totalCount = result.TotalCount;
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();

        string? sortBy = null;
        var sortDescending = false;
        if (args.Sorts?.Any() == true)
        {
            var sort = args.Sorts.First();
            sortBy = sort.Property;
            sortDescending = sort.SortOrder == SortOrder.Descending;
        }

        // Stale-while-revalidate: a cached view renders instantly with no spinner while the live fetch
        // revalidates; only a cold (uncached) view shows the spinner. A cache hit can race with an
        // in-flight SignalR heartbeat patch, but the background fetch overwrites it either way.
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, sortBy, sortDescending),
            () => Api.GetServersAsync(page, pageSize, _search, _typeFilter, _statusFilter, sortBy, sortDescending),
            ApplyServers,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private async Task ResetAndReload()
    {
        if (_grid is null) return;
        await _grid.GoToPage(0);
        // Radzen does not raise LoadData when GoToPage targets the already-selected first page.
        // Filters are normally changed from that page, so explicitly reload to make the new query
        // effective; the extra call is harmless when GoToPage already reloaded from a later page.
        await InvokeAsync(_grid.Reload);
    }

    internal async Task ClearFilters()
    {
        _search = null;
        _typeFilter = null;
        _statusFilter = null;
        await ResetAndReload();
    }

    internal async Task FilterByTag(string tag)
    {
        _search = tag;
        if (_grid is not null) await _grid.GoToPage(0);
    }

    private void GoToAddAgent() => Navigation.NavigateTo("/servers/add-agent");

    private Task OnContactAgent(ServerDto server)
        => Dialog.OpenAsync<ContactAgentDialog>(
            L["ContactAgent"],
            new Dictionary<string, object?> { { "ServerId", server.Id }, { "ServerName", server.Name } },
            new DialogOptions { Width = "480px", CloseDialogOnOverlayClick = false });

    private async Task OnUpdateAllAgents()
    {
        var confirmed = await Dialog.Confirm(
            L["UpdateAllAgentsConfirm"],
            L["UpdateAllAgents"],
            new ConfirmOptions { OkButtonText = L["UpdateAllAgents"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        var result = await Api.UpdateAllAgentsAsync();
        if (result is not null)
            Toast.Success("UpdateAllAgents", "UpdateAllAgentsQueued", result.QueuedCount);
        else
            Toast.Error("Error", "UpdateAgentFailed");
    }

    private async Task OnDeleteServer(ServerDto server)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteServerConfirm"], server.Name),
            L["DeleteServer"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        var deleted = await Api.DeleteServerAsync(server.Id);
        if (deleted)
        {
            Toast.Success(L["ServerDeleted"]);
            if (_grid is not null) await _grid.Reload();
        }
    }
}
