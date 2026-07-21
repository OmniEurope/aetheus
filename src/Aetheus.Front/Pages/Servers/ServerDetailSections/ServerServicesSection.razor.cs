// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerServicesSection : IAsyncDisposable
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ILogger<ServerServicesSection> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter] public int ServerId { get; set; }

    /// <summary>
    /// Raised when a service install/uninstall/lifecycle action targeting this server succeeds, so the
    /// host can force a server-detail refresh and reflect the new installed/running state immediately
    /// instead of waiting for the next ≤30 s heartbeat.
    /// </summary>
    [Parameter] public EventCallback OnServiceChanged { get; set; }

    // Install/uninstall route through the controlled-sudo package-manage operation, which the backend
    // refuses unless the agent reports the aetheus-package sudoers drop-in (derived capability).
    // Gate the UI on it so the buttons aren't live actions that always 400 with a generic error.
    internal bool CanManagePackages => Server.PackageManagementAvailable == true;

    // S-UX-OFFA: gate the grid's mutating actions when the agent is offline (a queued task would strand).
    private bool ServerOnline => Server.Status == ServerStatus.Online;

    // S-UX-INST: the apt-installed service whose install task is in flight; on success we offer to start it
    // (a freshly-installed service stays `dead` until an explicit start, e.g. inside the VPS-sim container).
    private string? _pendingStartAfterInstall;

    private string? _busyService;

    // Id of the task we are waiting on, so a completed task is matched by id rather than by name
    // suffix (which collides when two operations target the same service).
    private int? _busyTaskId;

    private const int MaxLogLines = 2000;

    private bool _logsVisible;
    private string? _logsServiceName;
    private readonly StringBuilder _logBuffer = new();
    private int _logLineCount;
    private string _logsContent = string.Empty;
    private bool _logsLoading;
    private int _logLines = 100;
    private bool _logFollow;
    private int? _currentLogTaskId;
    private HubConnection? _logHub;
    private readonly SemaphoreSlim _logStreamGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int? _activeServerId;
    private bool _disposed;

    private static readonly int[] LineOptions = [50, 100, 200, 500, 1000];

    private static readonly Dictionary<string, string> ServiceToModule = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docker"] = "docker",
        ["apache2"] = "apache",
        ["postfix"] = "mail",
        ["dovecot"] = "mail",
        ["portsentry"] = "portsentry",
        ["rkhunter"] = "rkhunter",
        ["certbot"] = "certbot",
        ["teamspeak"] = "teamspeak",
        ["ts3server"] = "teamspeak",
        ["cron"] = "cron",
        ["crond"] = "cron"
    };

    // Canonical (display name, module key) for modules with a dedicated Aetheus section.
    // Synthesised as "not installed" entries when the agent does not report any matching service.
    private static readonly (string DisplayName, string Module)[] DedicatedModules =
    [
        ("docker", "docker"),
        ("apache2", "apache"),
        ("certbot", "certbot"),
        ("postfix", "mail"),
        ("teamspeak", "teamspeak"),
        ("portsentry", "portsentry"),
        ("rkhunter", "rkhunter")
    ];

    private IEnumerable<ServiceInfoDto> SyntheticMissingModules =>
        DedicatedModules
            .Where(m => !Server.Services.Any(s => ServiceToModule.TryGetValue(s.Name, out var mod) && mod == m.Module))
            .Select(m => new ServiceInfoDto
            {
                Name = m.DisplayName,
                Type = ServiceType.Systemd,
                Status = string.Empty,
                IsRunning = false,
                IsManageable = true,
                IsInstalled = false
            });

    // Tab 1: Manageable + has dedicated Aetheus module (Docker, Apache, Mail, …)
    internal IEnumerable<ServiceInfoDto> ModuleInstalled =>
        Server.Services.Where(s => s.IsManageable && s.IsInstalled && HasDedicatedModule(s.Name)).OrderBy(s => s.Name);

    private IEnumerable<ServiceInfoDto> ModuleNotInstalled =>
        Server.Services.Where(s => s.IsManageable && !s.IsInstalled && HasDedicatedModule(s.Name))
            .Concat(SyntheticMissingModules)
            .OrderBy(s => s.Name);

    // Tab 2: Manageable but no dedicated module (nginx, mysql, fail2ban, …)
    private IEnumerable<ServiceInfoDto> OtherInstalled =>
        Server.Services.Where(s => s.IsManageable && s.IsInstalled && !HasDedicatedModule(s.Name)).OrderBy(s => s.Name);

    private IEnumerable<ServiceInfoDto> OtherNotInstalled =>
        Server.Services.Where(s => s.IsManageable && !s.IsInstalled && !HasDedicatedModule(s.Name)).OrderBy(s => s.Name);

    // Tab 3: System services (not manageable, read-only)
    private IEnumerable<ServiceInfoDto> SystemServices =>
        Server.Services.Where(s => !s.IsManageable).OrderBy(s => s.Name);

    internal int ModuleCount =>
        Server.Services.Count(s => s.IsManageable && HasDedicatedModule(s.Name)) + SyntheticMissingModules.Count();
    internal int OtherCount => Server.Services.Count(s => s.IsManageable && !HasDedicatedModule(s.Name));
    internal int SystemCount => Server.Services.Count(s => !s.IsManageable);

    internal static string FormatType(ServiceType type) => type switch
    {
        ServiceType.Systemd => "Systemd",
        ServiceType.Docker => "Docker",
        ServiceType.WindowsService => "Windows",
        _ => type.ToString()
    };

    /// <summary>
    /// Maps a service status string + IsRunning flag to a Radzen badge style. A oneshot/timer-driven
    /// service legitimately sits at rest with <c>scheduled</c> (paired .timer is active) or
    /// <c>idle</c> (cron-driven oneshot) - these are healthy states and must NOT show as Danger.
    /// </summary>
    internal static BadgeStyle GetStatusBadgeStyle(string status, bool isRunning)
    {
        if (isRunning) return BadgeStyle.Success;
        if (string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase)) return BadgeStyle.Info;
        if (string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase)) return BadgeStyle.Light;
        return BadgeStyle.Danger;
    }

    internal static bool HasDedicatedModule(string serviceName) =>
        ServiceToModule.ContainsKey(serviceName);

    protected override async Task OnParametersSetAsync()
    {
        if (_activeServerId == ServerId) return;
        await _logStreamGate.WaitAsync();
        try
        {
            await LeaveCurrentLogGroupCoreAsync();
            ResetTransientState();
            _activeServerId = ServerId;
        }
        finally
        {
            _logStreamGate.Release();
        }
    }

    public void HandleTaskCompleted(TaskCompletedNotification notification)
    {
        if (notification.ServerId != ServerId) return;
        // Correlate by task id: matching on the "- {service}" name suffix fired on the wrong
        // operation when an earlier task for the same service finished first.
        var matchesBusy = _busyTaskId is not null && notification.TaskId == _busyTaskId;
        if (matchesBusy)
        {
            _busyService = null;
            _busyTaskId = null;
            var pendingStart = _pendingStartAfterInstall;
            _pendingStartAfterInstall = null;

            // Surface the outcome immediately instead of leaving the row spinning until the next
            // heartbeat - the agent's task result tells us whether the operation actually succeeded.
            // Use Notify(severity, titleKey, rawMessage): the task name is a runtime string, not a
            // resx key, so it must NOT be run through the localizer (the *Key overloads localize both).
            if (notification.Status == TaskExecutionStatus.Success)
            {
                Toast.Notify(NotificationSeverity.Success, "TaskSucceeded", notification.TaskName);
                // Refresh the installed/running state now rather than waiting for the next heartbeat.
                _ = OnServiceChanged.InvokeAsync();
                // S-UX-INST: a just-installed service is stopped; offer to start it right away.
                if (pendingStart is { } justInstalled)
                    _ = PromptStartAfterInstallAsync(justInstalled);
            }
            else
            {
                var detail = notification.ExitCode is { } code
                    ? $"{notification.TaskName} (exit {code})"
                    : notification.TaskName;
                Toast.Notify(NotificationSeverity.Error, "TaskFailed", detail);
            }
        }

        var matchesLog = _currentLogTaskId == notification.TaskId;
        if (matchesLog)
        {
            _logsLoading = false;
            if (string.IsNullOrEmpty(_logsContent))
                _logsContent = notification.Output ?? L["NoLogsAvailable"];
        }

        // Fired from a SignalR callback (off the render loop) - re-render so the cleared busy
        // spinner / refreshed log pane actually appear without waiting for another interaction.
        if (matchesBusy || matchesLog)
            _ = InvokeAsync(StateHasChanged);
    }

    private void OpenServiceModule(string serviceName)
    {
        // Item #1: switched to real routes (/servers/{id}/{section}) instead of the legacy
        // ?section= query string.
        if (ServiceToModule.TryGetValue(serviceName, out var section))
            Nav.NavigateTo($"/servers/{ServerId}/{section}");
    }

    private async Task ConfirmAndExecuteAsync(ServiceAction action, string serviceName)
    {
        var msg = string.Format(L["ConfirmServiceAction"].Value, L[action.ToString()].Value, serviceName);
        var confirmed = await Dialog.Confirm(msg, L["Confirm"].Value,
            new ConfirmOptions { OkButtonText = L["Yes"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await ExecuteActionAsync(action, serviceName);
    }

    // Bulk bar: apply one action (Start/Stop/Restart/Install/Uninstall) to every checked service after a
    // single batch confirmation. Each service still becomes its own task on the agent; logs are excluded
    // (no bulk log view). Protected/unsupported targets just don't queue (reflected in the X/Y count).
    private async Task BulkActionAsync(BulkServiceRequest req)
    {
        if (req.Services.Count == 0) return;

        var verb = L[req.Kind].Value;
        var msg = string.Format(L["ConfirmBulkServiceAction"].Value, verb, req.Services.Count);
        var confirmed = await Dialog.Confirm(msg, L["Confirm"].Value,
            new ConfirmOptions { OkButtonText = L["Yes"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;
        if (!EnsureAgentOnline()) return;

        var queued = 0;
        foreach (var svc in req.Services)
        {
            // TeamSpeak installs via its module helper, not apt - route it to the setup endpoint
            // (which returns a status, not a task id) and count it as queued on success.
            if (req.Kind == "Install" && ManageableServiceGrid.IsModuleInstallable(svc))
            {
                if (await Api.SetupTeamspeakAsync(ServerId, new TeamspeakSetupRequest())) queued++;
                continue;
            }

            int? taskId = req.Kind switch
            {
                "Install" => await Api.InstallServiceAsync(ServerId, svc),
                "Uninstall" => await Api.UninstallServiceAsync(ServerId, svc),
                "Start" => await Api.ExecuteServiceActionAsync(ServerId, new ServiceActionRequest { Action = ServiceAction.Start, ServiceName = svc }),
                "Stop" => await Api.ExecuteServiceActionAsync(ServerId, new ServiceActionRequest { Action = ServiceAction.Stop, ServiceName = svc }),
                "Restart" => await Api.ExecuteServiceActionAsync(ServerId, new ServiceActionRequest { Action = ServiceAction.Restart, ServiceName = svc }),
                _ => null
            };
            if (taskId is not null) queued++;
        }

        var summary = string.Format(L["BulkServiceQueued"].Value, queued, req.Services.Count);
        if (queued == 0)
            Toast.Error(L["Error"], summary);
        else
            Toast.Success(L["TaskQueued"], summary);

        await OnServiceChanged.InvokeAsync();
    }

    private async Task ExecuteActionAsync(ServiceAction action, string serviceName)
    {
        _busyService = serviceName;
        var taskId = await Api.ExecuteServiceActionAsync(ServerId, new ServiceActionRequest
        {
            Action = action,
            ServiceName = serviceName
        });

        if (taskId is not null)
        {
            _busyTaskId = taskId;
            Toast.Success(L["TaskQueued"], $"{action} - {serviceName}");
        }
        else
        {
            Toast.Error(L["Error"], $"{action} - {serviceName}");
            _busyService = null;
        }
    }

    // The agent must be online to claim and run an install/uninstall task; queuing one for an offline
    // server would just strand it Pending (the backend rejects it too). Block early with a clear toast.
    private bool EnsureAgentOnline()
    {
        if (Server.Status == ServerStatus.Online) return true;
        Toast.Error(L["TaskAgentOffline"], L["AgentOfflineActionBlocked"]);
        return false;
    }

    private async Task InstallServiceAsync(string serviceName)
    {
        if (!EnsureAgentOnline()) return;

        var msg = string.Format(L["ConfirmInstallService"].Value, serviceName);
        var confirmed = await Dialog.Confirm(msg, L["Confirm"].Value,
            new ConfirmOptions { OkButtonText = L["Install"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        // TeamSpeak installs through its dedicated module helper (the typed TeamspeakSetup operation),
        // not apt - route it to the teamspeak setup endpoint with the documented defaults.
        if (ManageableServiceGrid.IsModuleInstallable(serviceName))
        {
            var ok = await Api.SetupTeamspeakAsync(ServerId, new TeamspeakSetupRequest());
            if (ok)
            {
                Toast.Success(L["TaskQueued"], $"Install - {serviceName}");
                await OnServiceChanged.InvokeAsync();
            }
            else
            {
                Toast.Error(L["Error"], $"Install - {serviceName}");
            }
            return;
        }

        _busyService = serviceName;
        var taskId = await Api.InstallServiceAsync(ServerId, serviceName);

        if (taskId is not null)
        {
            _busyTaskId = taskId;
            _pendingStartAfterInstall = serviceName;   // S-UX-INST: offer to start it once the install lands
            Toast.Success(L["TaskQueued"], $"Install - {serviceName}");
        }
        else
        {
            Toast.Error(L["Error"], $"Install - {serviceName}");
            _busyService = null;
        }
    }

    // S-UX-INST: after a successful install, an apt service is present but not started. Offer a one-click
    // start rather than leaving the user to hunt for the Start button in the (now Installed) grid.
    private async Task PromptStartAfterInstallAsync(string serviceName)
    {
        if (Server.Status != ServerStatus.Online) return;
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmStartAfterInstall"].Value, serviceName),
            L["ServiceInstalledTitle"].Value,
            new ConfirmOptions { OkButtonText = L["Start"].Value, CancelButtonText = L["Later"].Value });
        if (confirmed == true)
            await ExecuteActionAsync(ServiceAction.Start, serviceName);
    }

    private async Task UninstallServiceAsync(string serviceName)
    {
        if (!EnsureAgentOnline()) return;

        var msg = string.Format(L["ConfirmUninstallService"].Value, serviceName);
        var confirmed = await Dialog.Confirm(msg, L["Confirm"].Value,
            new ConfirmOptions { OkButtonText = L["Uninstall"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        _busyService = serviceName;
        var taskId = await Api.UninstallServiceAsync(ServerId, serviceName);

        if (taskId is not null)
        {
            _busyTaskId = taskId;
            Toast.Success(L["TaskQueued"], $"Uninstall - {serviceName}");
        }
        else
        {
            Toast.Error(L["Error"], $"Uninstall - {serviceName}");
            _busyService = null;
        }
    }

    private async Task ViewServiceLogsAsync(string serviceName)
    {
        _logsServiceName = serviceName;
        _logsVisible = true;
        await StartLogStreamAsync();
    }

    private async Task StartLogStreamAsync()
    {
        if (_logsServiceName is null || _disposed) return;
        try
        {
            await _logStreamGate.WaitAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (_logsServiceName is null || _disposed) return;
            var serverId = ServerId;
            var serviceName = _logsServiceName;
            await LeaveCurrentLogGroupCoreAsync();

            _logBuffer.Clear();
            _logLineCount = 0;
            _logsContent = string.Empty;
            _logsLoading = true;
            StateHasChanged();

            var taskId = await Api.RequestServiceLogsAsync(
                serverId, serviceName, _logLines, _logFollow, _lifetimeCts.Token);
            if (_disposed || serverId != ServerId || serviceName != _logsServiceName)
                return;
            if (taskId is null)
            {
                Toast.Error(L["Error"], $"Logs - {serviceName}");
                _logsLoading = false;
                return;
            }

            _currentLogTaskId = taskId;
            await EnsureLogHubAsync();
            try
            {
                await _logHub!.InvokeAsync("JoinTaskGroup", taskId, _lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[ServerServices] JoinTaskGroup failed");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        finally
        {
            _logStreamGate.Release();
        }
    }

    private async Task EnsureLogHubAsync()
    {
        if (_logHub is not null) return;

        _logHub = HubFactory.Create("logs");
        _logHub.On<TaskLogDto>("LogReceived", log =>
        {
            if (log.TaskId != _currentLogTaskId) return;
            AppendLog(log.Message);
            _logsLoading = false;
            _ = InvokeAsync(StateHasChanged);
        });

        _logHub.On<List<TaskLogDto>>("LogsReceived", logs =>
        {
            var batch = logs.Where(l => l.TaskId == _currentLogTaskId).ToList();
            if (batch.Count == 0) return;
            foreach (var log in batch)
                AppendLog(log.Message);
            _logsLoading = false;
            _ = InvokeAsync(StateHasChanged);
        });

        // Group membership is per-connection and lost on auto-reconnect - re-join the current
        // task's log group so streaming continues after a transient drop.
        _logHub.RejoinOnReconnect(async () =>
        {
            if (_currentLogTaskId > 0)
                await _logHub.InvokeAsync("JoinTaskGroup", _currentLogTaskId);
        });

        await _logHub.StartAsync();
    }

    private async Task LeaveCurrentLogGroupCoreAsync()
    {
        if (_logHub is null || _currentLogTaskId is null) return;
        try
        {
            await _logHub.InvokeAsync("LeaveTaskGroup", _currentLogTaskId);
        }
        catch (Exception ex) { Logger.LogWarning(ex, "[ServerServices] LeaveTaskGroup failed"); }
        _currentLogTaskId = null;
    }

    private void AppendLog(string? message)
    {
        if (message is null) return;
        if (_logLineCount >= MaxLogLines)
        {
            _logBuffer.Clear();
            _logLineCount = 0;
            _logBuffer.AppendLine("[... truncated - max lines reached, refreshing ...]");
            _logLineCount++;
        }
        _logBuffer.AppendLine(message);
        _logLineCount++;
        _logsContent = _logBuffer.ToString();
    }

    // S-UX-M6T2: journalctl run by an agent that is not in the systemd-journal/adm group returns a
    // benign-looking notice instead of real entries. Detect those known patterns so the UI can show a
    // clear, actionable hint rather than leaving the raw journalctl notice in the log pane.
    internal bool HasInsufficientJournalPermissions =>
        !string.IsNullOrEmpty(_logsContent) &&
        (_logsContent.Contains("No journal files were opened due to insufficient permissions", StringComparison.OrdinalIgnoreCase)
         || _logsContent.Contains("Failed to open files: Permission denied", StringComparison.OrdinalIgnoreCase)
         || (_logsContent.Contains("not seeing messages", StringComparison.OrdinalIgnoreCase)
             && _logsContent.Contains("systemd-journal", StringComparison.OrdinalIgnoreCase)));

    private async Task RefreshLogsAsync() => await StartLogStreamAsync();

    private async Task OnLogLinesChanged(int lines)
    {
        _logLines = lines;
        await StartLogStreamAsync();
    }

    private async Task OnFollowChangedAsync(bool follow)
    {
        _logFollow = follow;
        await StartLogStreamAsync();
    }

    private async Task CloseLogsAsync()
    {
        await _logStreamGate.WaitAsync();
        try
        {
            await LeaveCurrentLogGroupCoreAsync();
            ResetLogState();
        }
        finally
        {
            _logStreamGate.Release();
        }
    }

    private void ResetLogState()
    {
        _logsVisible = false;
        _logsServiceName = null;
        _currentLogTaskId = null;
        _logBuffer.Clear();
        _logLineCount = 0;
        _logsContent = string.Empty;
        _logsLoading = false;
        _logFollow = false;
        _logLines = 100;
    }

    private void ResetTransientState()
    {
        _pendingStartAfterInstall = null;
        _busyService = null;
        _busyTaskId = null;
        ResetLogState();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        await _logStreamGate.WaitAsync();
        try
        {
            await LeaveCurrentLogGroupCoreAsync();
            if (_logHub is not null)
                await _logHub.DisposeAsync();
        }
        finally
        {
            _logStreamGate.Release();
            _lifetimeCts.Dispose();
            _logStreamGate.Dispose();
        }
    }
}
