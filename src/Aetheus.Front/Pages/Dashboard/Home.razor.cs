// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Web;

namespace Aetheus.Front.Pages.Dashboard;

public partial class Home : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ILogger<Home> Logger { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private TaskTrackerService TaskTracker { get; set; } = default!;

    private DashboardOverviewDto? _dashboard;
    private AppMonitoringSummaryDto? _appSummary;
    private HubConnection? _serversHub;
    private HubConnection? _pipelinesHub;
    private HubConnection? _entitiesHub;
    private bool _reloading;
    private readonly CancellationTokenSource _lifetimeCts = new();

    // A single server or pipeline transition can emit several hub events in quick succession.
    // Collapse those bursts into one trailing dashboard request instead of refetching the same
    // payload for every notification.
    private readonly TrailingReloadCoalescer _dashboardCoalescer = new(1000);

    // ADR-021: app-status changes broadcast a Project "Updated" on the entities hub. Coalesce the burst
    // (many apps flipping at once) into one trailing reload of the monitoring tile.
    private readonly TrailingReloadCoalescer _appSummaryCoalescer = new(2000);
    private const string DashboardCacheKey = "dashboard:overview";
    private const string AppSummaryCacheKey = "dashboard:app-summary";

    private bool CanViewDashboard =>
        Permissions.CanReadAny(ResourceType.Server)
        || Permissions.CanReadAny(ResourceType.Project)
        || Permissions.CanReadAny(ResourceType.Pipeline);

    // S-UX-CAPI: a capability icon on the Servers tile deep-links to that server's matching detail section
    // (build → pipelines, deploy → apps, manage → modules).
    private void NavToSection(int serverId, string section) => Nav.NavigateTo($"/servers/{serverId}/{section}");

    private void OnCapKey(KeyboardEventArgs e, int serverId, string section)
    {
        if (e.Key is "Enter" or " ") NavToSection(serverId, section);
    }

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAuthenticated)
        {
            Nav.NavigateTo("/login");
            return;
        }

        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        TaskTracker.OnChanged += OnTrackedTasksChanged;

        // A user without any readable dashboard resource gets the empty state without leaking
        // aggregate counts or opening realtime subscriptions for data they cannot access.
        if (Permissions.IsLoaded && !CanViewDashboard)
            return;

        Cache.Seed<DashboardOverviewDto>(DashboardCacheKey, value => _dashboard = value);
        Cache.Seed<AppMonitoringSummaryDto>(AppSummaryCacheKey, value => _appSummary = value);

        try
        {
            _dashboard = await Api.Monitoring.GetDashboardAsync();
            if (_dashboard is not null) Cache.Set(DashboardCacheKey, _dashboard);
        }
        catch (HttpRequestException ex)
        {
            // Handled by ErrorNotificationHandler/AuthDelegatingHandler
            Logger.LogWarning(ex, "[Home] Dashboard load failed");
        }

        try
        {
            _appSummary = await Api.Monitoring.GetAppMonitoringSummaryAsync();
            if (_appSummary is not null) Cache.Set(AppSummaryCacheKey, _appSummary);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "[Home] App monitoring summary load failed");
        }

        await ConnectHubsAsync();
    }

    private void OnPermissionsChanged() => InvokeAsync(StateHasChanged);

    private void OnTrackedTasksChanged()
    {
        if (Auth.IsAdmin)
            _ = _dashboardCoalescer.RequestAsync(ReloadAsync, _lifetimeCts.Token);
    }

    // Keep the dashboard cards and grids fresh: server lifecycle/heartbeat changes the
    // online/offline counts, pipeline run events change "recent runs". Best-effort - a hub
    // failure leaves the page static (still loaded once).
    private async Task ConnectHubsAsync()
    {
        try
        {
            _serversHub = HubFactory.Create("servers");
            _serversHub.On<ServerDto>("ServerRegistered", _ => RequestDashboardReloadAsync());
            _serversHub.On<int>("ServerRemoved", _ => RequestDashboardReloadAsync());
            _serversHub.On<int>("ServerOffline", _ => RequestDashboardReloadAsync());
            _serversHub.On<int, ServerHeartbeatDto>("ServerHeartbeat", (_, _) => RequestDashboardReloadAsync());
            _serversHub.RejoinOnReconnect(async () =>
            {
                await _serversHub.InvokeAsync("JoinAllServers");
                await ReloadAsync();
            });
            await _serversHub.StartAsync();
            await _serversHub.InvokeAsync("JoinAllServers");
        }
        catch { /* best-effort */ }

        try
        {
            _pipelinesHub = HubFactory.Create("pipelines");
            _pipelinesHub.On<int, int>("PipelineRunStarted", (_, _) => RequestDashboardReloadAsync());
            _pipelinesHub.On<int, PipelineStatus>("PipelineRunCompleted", (_, _) => RequestDashboardReloadAsync());
            _pipelinesHub.On<int>("PipelineRunCancelled", _ => RequestDashboardReloadAsync());
            _pipelinesHub.RejoinOnReconnect(async () =>
            {
                await _pipelinesHub.InvokeAsync("JoinPipelineUpdatesGroup");
                await ReloadAsync();
            });
            await _pipelinesHub.StartAsync();
            await _pipelinesHub.InvokeAsync("JoinPipelineUpdatesGroup");
        }
        catch { /* best-effort */ }

        try
        {
            _entitiesHub = HubFactory.Create("entities");
            _entitiesHub.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
                type == ResourceType.Project
                    ? _appSummaryCoalescer.RequestAsync(ReloadAppSummaryAsync, _lifetimeCts.Token)
                    : Task.CompletedTask);
            _entitiesHub.RejoinOnReconnect(async () =>
            {
                await _entitiesHub.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
                await ReloadAppSummaryAsync();
            });
            await _entitiesHub.StartAsync();
            await _entitiesHub.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
        }
        catch { /* best-effort */ }
    }

    private Task ReloadAppSummaryAsync() => InvokeAsync(async () =>
    {
        try
        {
            _appSummary = await Api.Monitoring.GetAppMonitoringSummaryAsync();
            if (_appSummary is not null) Cache.Set(AppSummaryCacheKey, _appSummary);
        }
        catch (HttpRequestException) { /* hub-triggered reload - silent on auth failure */ }
        StateHasChanged();
    });

    private Task RequestDashboardReloadAsync() =>
        _dashboardCoalescer.RequestAsync(ReloadAsync, _lifetimeCts.Token);

    private Task ReloadAsync()
    {
        if (_reloading) return Task.CompletedTask;
        _reloading = true;
        return InvokeAsync(async () =>
        {
            try
            {
                _dashboard = await Api.Monitoring.GetDashboardAsync();
                if (_dashboard is not null) Cache.Set(DashboardCacheKey, _dashboard);
            }
            catch (HttpRequestException) { /* hub-triggered reload - silent on auth failure */ }
            finally { _reloading = false; }
            StateHasChanged();
        });
    }

    private static BadgeStyle GetRunBadge(PipelineStatus status) => PipelineHelper.GetRunBadge(status);

    private static BadgeStyle GetAppHealthBadge(AppHealthStatus status) => status switch
    {
        AppHealthStatus.Up => BadgeStyle.Success,
        AppHealthStatus.Down => BadgeStyle.Danger,
        AppHealthStatus.Degraded => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        TaskTracker.OnChanged -= OnTrackedTasksChanged;
        if (_serversHub is not null)
        {
            try { await _serversHub.InvokeAsync("LeaveAllServers"); } catch { /* best-effort */ }
            await _serversHub.DisposeAsync();
        }
        if (_pipelinesHub is not null)
        {
            try { await _pipelinesHub.InvokeAsync("LeavePipelineUpdatesGroup"); } catch { /* best-effort */ }
            await _pipelinesHub.DisposeAsync();
        }
        if (_entitiesHub is not null)
        {
            try { await _entitiesHub.InvokeAsync("LeaveEntityUpdates", ResourceType.Project); } catch { /* best-effort */ }
            await _entitiesHub.DisposeAsync();
        }
        _lifetimeCts.Dispose();
    }
}
