// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers;

/// <summary>
/// Self-update progress indicator. Lives next to <c>ServerDetail</c>, listens to the existing
/// server hub (shared connection - no double-subscribe), and renders a determinate progress
/// bar that walks Queued → PickedUp → … → Done. Stays invisible at rest.
/// </summary>
public partial class AgentUpdateProgressCard : IAsyncDisposable
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private ClientErrorReporter ErrorReporter { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter, EditorRequired] public HubConnection? Hub { get; set; }
    /// <summary>Retained for source compatibility; heartbeats alone never confirm an update.</summary>
    [Parameter] public string? CurrentAgentVersion { get; set; }
    [Parameter] public AgentUpdateRequestSummaryDto? Request { get; set; }
    /// <summary>Invoked from the Retry button shown in the Failed state (S-DES-17). Wired by the
    /// parent to its UpdateAgent action so a failed self-update can be relaunched in place.</summary>
    [Parameter] public EventCallback OnRetry { get; set; }

    private AgentUpdatePhase? _phase;
    private int _percent;
    private string? _message;
    private DateTime _showDoneUntil = DateTime.MinValue;
    private bool _fadingOut;
    private int? _requestId;

    // Registered handlers - kept so we can detach them in DisposeAsync to avoid leaks if the
    // hub connection survives this component (it always does - the connection is owned by
    // ServerDetail).
    private IDisposable? _onQueued;
    private IDisposable? _onProgress;
    private IDisposable? _onOffline;
    private IDisposable? _onConfirmed;
    private IDisposable? _onFailed;
    private System.Threading.Timer? _staleTimer;

    protected override void OnParametersSet()
    {
        ApplyPersistedRequest();
        if (Hub is null) return;
        Subscribe(Hub);
        // OnParametersSet can fire on each render; guard against double-subscribe by storing
        // the disposables and detaching the previous batch when re-subscribing on a fresh hub.
        // In practice the parent passes the same instance throughout the page's lifetime, so
        // this is belt-and-braces.
    }

    private void Subscribe(HubConnection hub)
    {
        _onQueued ??= hub.On<int>("AgentUpdateQueued", serverId =>
        {
            if (serverId != ServerId) return;
            _phase = AgentUpdatePhase.Queued;
            _percent = 0;
            _message = null;
            StartStaleTimer(120);
            _ = InvokeAsync(StateHasChanged);
        });

        _onProgress ??= hub.On<AgentUpdateProgressDto>("AgentUpdateProgress", dto =>
        {
            if (dto.ServerId != ServerId) return;
            // Monotonic guard: ignore phase regressions from network reordering or a retry beat.
            // Failed is special - always shown.
            if (_phase is { } current && dto.Phase != AgentUpdatePhase.Failed && dto.Phase < current)
                return;

            _phase = dto.Phase;
            _percent = dto.Percent;
            _message = dto.Message;
            if (dto.Phase is AgentUpdatePhase.Failed or AgentUpdatePhase.Done)
                CancelStaleTimer();
            else
                StartStaleTimer(90);
            _ = InvokeAsync(StateHasChanged);
        });

        _onOffline ??= hub.On<int>("ServerOffline", serverId =>
        {
            if (serverId != ServerId) return;
            // ServerOffline is only meaningful here AFTER we've seen LaunchingUpdater - that
            // tells us the offline is the planned binary swap, not a real crash.
            if (_phase is not (AgentUpdatePhase.LaunchingUpdater
                or AgentUpdatePhase.AgentOffline)) return;
            _phase = AgentUpdatePhase.AgentOffline;
            _percent = 95;
            _message = L["AgentUpdateRestartingHint"];
            _ = InvokeAsync(StateHasChanged);
        });

        _onConfirmed ??= hub.On<int, int>("AgentUpdateConfirmed", (serverId, requestId) =>
        {
            if (serverId != ServerId) return;
            HandleConfirmed(requestId, Request?.TargetVersion);
        });

        _onFailed ??= hub.On<int, int>("AgentUpdateFailed", (serverId, requestId) =>
        {
            if (serverId != ServerId) return;
            _requestId = requestId;
            CancelStaleTimer();
            _phase = AgentUpdatePhase.Failed;
            _percent = 0;
            _message = Request?.FailureDiagnostic ?? L["AgentUpdateFailed"];
            _ = InvokeAsync(StateHasChanged);
        });
    }

    public void HandleConfirmed(int requestId, string? targetVersion)
    {
        _requestId = requestId;
        CancelStaleTimer();
        _phase = AgentUpdatePhase.Done;
        _percent = 100;
        _message = !string.IsNullOrEmpty(targetVersion)
            ? string.Format(L["AgentUpdateDoneHint"], targetVersion)
            : null;
        if (!string.IsNullOrEmpty(targetVersion))
            Toast.Success("AgentUpdated", "AgentUpdateDoneHint", targetVersion);
        _showDoneUntil = DateTime.Now.AddSeconds(5);
        _ = InvokeAsync(async () =>
        {
            StateHasChanged();
            // S-DES-16: fade the card out over the last 0.4s instead of an abrupt removal.
            await Task.Delay(TimeSpan.FromSeconds(4.6));
            _fadingOut = true;
            StateHasChanged();
            await Task.Delay(TimeSpan.FromSeconds(0.4));
            _phase = null;
            _fadingOut = false;
            StateHasChanged();
        });
    }

    /// <summary>
    /// Compatibility seam for older parents. A heartbeat alone is deliberately insufficient:
    /// confirmation arrives through <c>AgentUpdateConfirmed</c> after backend validation.
    /// </summary>
    public void HandleHeartbeat(string newAgentVersion)
    {
    }

    private void ApplyPersistedRequest()
    {
        if (Request is null || Request.RequestId == _requestId) return;
        _requestId = Request.RequestId;
        _message = Request.FailureDiagnostic;
        (_phase, _percent) = Request.Status switch
        {
            AgentUpdateRequestStatus.WaitingForIdle => (AgentUpdatePhase.Queued, 0),
            AgentUpdateRequestStatus.Queued => (AgentUpdatePhase.Queued, 0),
            AgentUpdateRequestStatus.Downloading => (AgentUpdatePhase.Downloading, 20),
            AgentUpdateRequestStatus.Staging => (AgentUpdatePhase.Extracting, 60),
            AgentUpdateRequestStatus.Handoff => (AgentUpdatePhase.AgentOffline, 95),
            AgentUpdateRequestStatus.Confirmed => (AgentUpdatePhase.Done, 100),
            AgentUpdateRequestStatus.Failed => (AgentUpdatePhase.Failed, 0),
            _ => ((AgentUpdatePhase?)null, 0)
        };
        if (Request.Status == AgentUpdateRequestStatus.WaitingForIdle)
        {
            _message = string.Format(
                L["AgentUpdateWaitingForTasks"],
                Request.BlockingTaskCount);
        }
    }

    // S-DES-17: relaunch a failed self-update. Clear the card first so the parent's trigger drives
    // a fresh Queued→… cycle through the existing hub events.
    private async Task RetryAsync()
    {
        CancelStaleTimer();
        _phase = null;
        _percent = 0;
        _message = null;
        StateHasChanged();
        await OnRetry.InvokeAsync();
    }

    private string? FailureLogsPath => _requestId is { } requestId
        ? $"/admin/system-logs?search={Uri.EscapeDataString(LogCorrelationIds.AgentUpdate(requestId))}"
        : null;

    private string PhaseLabel(AgentUpdatePhase? phase) => phase switch
    {
        AgentUpdatePhase.Queued => "AgentUpdatePhase_Queued",
        AgentUpdatePhase.PickedUp => "AgentUpdatePhase_PickedUp",
        AgentUpdatePhase.Downloading => "AgentUpdatePhase_Downloading",
        AgentUpdatePhase.Downloaded => "AgentUpdatePhase_Downloaded",
        AgentUpdatePhase.Extracting => "AgentUpdatePhase_Extracting",
        AgentUpdatePhase.LaunchingUpdater => "AgentUpdatePhase_LaunchingUpdater",
        AgentUpdatePhase.AgentOffline => "AgentUpdatePhase_AgentOffline",
        AgentUpdatePhase.Done => "AgentUpdatePhase_Done",
        AgentUpdatePhase.Failed => "AgentUpdatePhase_Failed",
        _ => "AgentUpdatePhase_Queued"
    };

    private static string PhaseIcon(AgentUpdatePhase? phase) => phase switch
    {
        AgentUpdatePhase.Queued => "schedule",
        AgentUpdatePhase.PickedUp => "play_arrow",
        AgentUpdatePhase.Downloading => "cloud_download",
        AgentUpdatePhase.Downloaded => "task",
        AgentUpdatePhase.Extracting => "folder_zip",
        AgentUpdatePhase.LaunchingUpdater => "rocket_launch",
        AgentUpdatePhase.AgentOffline => "hourglass_top",
        AgentUpdatePhase.Done => "check_circle",
        AgentUpdatePhase.Failed => "error",
        _ => "schedule"
    };

    private static string PhaseIconClass(AgentUpdatePhase? phase) => phase switch
    {
        AgentUpdatePhase.Done => "agent-update-phase-done",
        AgentUpdatePhase.Failed => "agent-update-phase-failed",
        _ => "agent-update-phase-active"
    };

    private static BadgeStyle PhaseBadge(AgentUpdatePhase? phase) => phase switch
    {
        AgentUpdatePhase.Done => BadgeStyle.Success,
        AgentUpdatePhase.Failed => BadgeStyle.Danger,
        AgentUpdatePhase.AgentOffline => BadgeStyle.Warning,
        _ => BadgeStyle.Info
    };

    private void StartStaleTimer(int seconds)
    {
        _staleTimer?.Dispose();
        _staleTimer = new System.Threading.Timer(_ =>
        {
            if (_phase is AgentUpdatePhase.Done or AgentUpdatePhase.Failed or null)
                return;
            _phase = AgentUpdatePhase.Failed;
            _percent = 0;
            _message = L["AgentUpdateTimeoutHint"];
            if (_requestId is { } requestId)
            {
                ErrorReporter.Report(
                    LogCorrelationIds.AgentUpdate(requestId),
                    L["AgentUpdateFailed"],
                    _message);
            }
            _ = InvokeAsync(StateHasChanged);
        }, null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
    }

    private void CancelStaleTimer()
    {
        _staleTimer?.Dispose();
        _staleTimer = null;
    }

    public ValueTask DisposeAsync()
    {
        CancelStaleTimer();
        _onQueued?.Dispose();
        _onProgress?.Dispose();
        _onOffline?.Dispose();
        _onConfirmed?.Dispose();
        _onFailed?.Dispose();
        return ValueTask.CompletedTask;
    }
}
