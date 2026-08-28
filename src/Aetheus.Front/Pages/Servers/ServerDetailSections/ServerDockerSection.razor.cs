// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

/// <summary>
/// The Docker section of a server's detail page. Every tab is now its own component; what stays here
/// is what no single tab can own: the inventory refresh cycle (manual and auto), the prune dialog that
/// spans all resource kinds, the component lifetime, and the SignalR entry point that forwards task
/// output to whichever tab was waiting for it.
/// </summary>
public partial class ServerDockerSection : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter] public bool DockerInitialLoaded { get; set; }
    [Parameter] public EventCallback<ServerDetailDto> ServerChanged { get; set; }

    private bool _dockerRefreshing;
    private bool _dockerAutoRefresh;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _autoRefreshCts;
    private Task? _autoRefreshTask;
    internal bool IsAutoRefreshRunning => _autoRefreshTask is not null && _autoRefreshCts is { IsCancellationRequested: false };
    private int? _activeServerId;
    private bool _disposed;

    private bool _pruning;

    private bool _dockerInitialLoaded;

    // The two tabs that consume task output of their own; the parent keeps a handle on each to forward.
    private DockerContainersTab? _containersTab;
    private DockerComposeTab? _composeTab;

    protected override void OnParametersSet()
    {
        if (_activeServerId != ServerId)
        {
            _activeServerId = ServerId;
            StopAutoRefresh();
            _dockerAutoRefresh = false;
            ResetServerState();
            _dockerInitialLoaded = DockerInitialLoaded;
        }
        else if (DockerInitialLoaded)
            _dockerInitialLoaded = true;
    }

    public void HandleTaskCompleted(TaskCompletedNotification notification)
    {
        if (notification.ServerId != ServerId) return;
        if (notification.Output is not null)
            DispatchTaskOutput(notification.TaskName, notification.Output);

        // HTC5: HandleTaskCompleted is invoked from the SignalR callback (outside the render loop),
        // so marshal the re-render back onto the renderer's sync context - as the Apache/Mail/Services
        // sections already do - otherwise the real-time update silently never paints.
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Hands the raw task output to every tab that consumes any; each decides whether it was waiting
    /// for this one. The parent deliberately knows nothing about task names.
    /// </summary>
    private void DispatchTaskOutput(string taskName, string output)
    {
        _containersTab?.HandleTaskOutput(taskName, output);
        _composeTab?.HandleTaskOutput(taskName, output);
    }

    private void OnAutoRefreshChanged(bool value)
    {
        _dockerAutoRefresh = value;
        if (value)
        {
            StopAutoRefresh();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _autoRefreshCts = cts;
            _autoRefreshTask = RunAutoRefreshAsync(cts);
        }
        else
            StopAutoRefresh();
    }

    private async Task RunAutoRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cts.Token))
                await InvokeAsync(RefreshDockerAsync);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_autoRefreshCts, cts))
            {
                _autoRefreshCts = null;
                _autoRefreshTask = null;
            }
            cts.Dispose();
        }
    }

    private void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
    }

    private async Task RefreshDockerAsync()
    {
        if (_disposed || !await _refreshGate.WaitAsync(0)) return;
        var serverId = ServerId;
        var serverSnapshot = Server;
        _dockerRefreshing = true;
        try
        {
            var containers = await Api.ServerTools.GetDockerContainersAsync(serverId, _lifetimeCts.Token);
            if (_disposed || _activeServerId != serverId || ServerId != serverId) return;
            var updated = serverSnapshot with { Docker = serverSnapshot.Docker with { Containers = containers } };
            await ServerChanged.InvokeAsync(updated);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        finally
        {
            if (_activeServerId == serverId) _dockerRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task OpenPruneDialogAsync()
    {
        var result = await Dialog.OpenAsync<DockerPruneDialog>(
            L["DockerPruneDialog"].Value,
            options: new DialogOptions { Width = "32rem", AutoFocusFirstElement = false });
        if (result is DockerPruneDialogResult selection)
            await PruneAsync(selection.Containers, selection.Images, selection.Volumes);
    }

    private async Task PruneAsync(bool containers, bool images, bool volumes)
    {
        _pruning = true;
        // finally, not a trailing assignment: PruneDockerAsync reaches the network without catching, so
        // an HttpRequestException used to leave the flag stuck at true and the button disabled until the
        // component was rebuilt.
        try
        {
            var request = new DockerPruneRequest { Containers = containers, Images = images, Volumes = volumes };
            var success = await Api.ServerTools.PruneDockerAsync(ServerId, request);
            if (success)
            {
                Toast.Success("DockerPrune", "DockerPruneQueued");
            }
            else
            {
                Toast.Error("DockerPrune", "DockerPruneFailed");
            }
        }
        finally
        {
            _pruning = false;
        }
    }

    /// <summary>
    /// Clears what the parent itself carries across a server change. Per-tab state is not reset here:
    /// every tab carries <c>@key="ServerId"</c> and is rebuilt from scratch instead.
    /// </summary>
    private void ResetServerState()
    {
        _dockerRefreshing = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        StopAutoRefresh();
        var autoRefreshTask = _autoRefreshTask;
        if (autoRefreshTask is not null)
            await autoRefreshTask;
        await _refreshGate.WaitAsync();
        _refreshGate.Release();
        _lifetimeCts.Dispose();
        _refreshGate.Dispose();
    }
}
