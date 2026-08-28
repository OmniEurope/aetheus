// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerRkhunterSection
{
    [Parameter] public int ServerId { get; set; }
    [Parameter] public RkhunterDataDto Rk { get; set; } = new();
    [Parameter] public EventCallback OnRefreshRequested { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    private bool _actionRunning;
    private string _setupMailOnWarning = string.Empty;
    private List<RkhunterWarningDto> _warnings = [];
    private List<RkhunterScanResultDto> _scanHistory = [];
    private string _scheduleCron = string.Empty;
    private bool _savingSchedule;
    private int? _loadedServerId;
    private bool _scheduleDirty;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId != ServerId)
        {
            _loadedServerId = ServerId;
            _warnings = [];
            _scanHistory = [];
            _setupMailOnWarning = string.Empty;
            _scheduleCron = Rk.ScanScheduleCron ?? string.Empty;
            _scheduleDirty = false;
            if (Rk.IsInstalled)
                await LoadWarningsAsync();
            return;
        }

        if (!_scheduleDirty)
            _scheduleCron = Rk.ScanScheduleCron ?? string.Empty;
    }

    private void OnScheduleChanged(string? value)
    {
        _scheduleCron = value ?? string.Empty;
        _scheduleDirty = true;
    }

    private async Task ExecuteActionAsync(RkhunterAction action)
    {
        _actionRunning = true;
        var success = await Api.Security.ExecuteRkhunterActionAsync(ServerId, new RkhunterActionRequest { Action = action });
        _actionRunning = false;
        if (success)
        {
            Toast.Success("TaskCreated");
            await OnRefreshRequested.InvokeAsync();
        }
        else
            Toast.Error("Error", "ActionFailed");
    }

    private async Task SetupAsync()
    {
        var request = new RkhunterSetupRequest
        {
            MailOnWarning = _setupMailOnWarning?.Trim() ?? string.Empty
        };
        var success = await Api.Security.SetupRkhunterAsync(ServerId, request);
        if (success)
        {
            Toast.Success("TaskCreated");
            await OnRefreshRequested.InvokeAsync();
        }
        else
            Toast.Error("Error", "SetupFailed");
    }

    private async Task OpenSetupDialogAsync()
    {
        var result = await Dialog.OpenAsync<ServerSetupDialog>(
            L["RkhunterSetup"].Value,
            new Dictionary<string, object?>
            {
                { "Kind", ServerSetupKind.Rkhunter },
                { "Model", new ServerSetupDialogModel { MailOnWarning = _setupMailOnWarning } }
            },
            new DialogOptions { Width = "32rem", AutoFocusFirstElement = false });
        if (result is not ServerSetupDialogModel model) return;
        _setupMailOnWarning = model.MailOnWarning;
        await SetupAsync();
    }

    private async Task GetLogsAsync()
    {
        var success = await Api.Security.GetRkhunterLogsAsync(ServerId, new RkhunterLogRequest());
        if (success)
            Toast.Success("TaskCreated");
    }

    private async Task LoadWarningsAsync()
    {
        _warnings = await Api.Security.GetRkhunterWarningsAsync(ServerId);
    }

    private async Task LoadScanHistoryAsync()
    {
        _scanHistory = await Api.Security.GetRkhunterScanHistoryAsync(ServerId);
    }

    /// <summary>
    /// Item #10.3 - hook for ServerDetail's TaskCompleted dispatcher. Refresh scan history +
    /// warnings whenever a RKHunter task completes so the UI lands on fresh data without the
    /// user manually hitting "Refresh".
    /// </summary>
    public async Task HandleTaskCompletedAsync(TaskCompletedNotification notification)
    {
        if (!notification.TaskName.Contains("RKHunter", StringComparison.OrdinalIgnoreCase))
            return;
        await LoadScanHistoryAsync();
        await LoadWarningsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveScheduleAsync()
    {
        _savingSchedule = true;
        try
        {
            var cron = string.IsNullOrWhiteSpace(_scheduleCron) ? null : _scheduleCron.Trim();
            var success = await Api.Security.SetRkhunterScheduleAsync(ServerId, new RkhunterScheduleRequest { CronExpression = cron });
            if (success)
            {
                _scheduleCron = cron ?? string.Empty;
                _scheduleDirty = false;
                Toast.Success("Saved");
                await OnRefreshRequested.InvokeAsync();
            }
            else
                Toast.Error("Error", "SaveFailed");
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "SaveFailed");
        }
        finally
        {
            _savingSchedule = false;
        }
    }

    private static string GetScanStatusIcon(string status) => status switch
    {
        "clean" => "check_circle",
        "warning" => "warning",
        _ => "help"
    };

    private static string GetScanStatusColor(string status) => status switch
    {
        "clean" => "scan-status-clean",
        "warning" => "scan-status-warning",
        _ => "scan-status-unknown"
    };

    private static BadgeStyle GetScanBadgeStyle(string status) => status switch
    {
        "clean" => BadgeStyle.Success,
        "warning" => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    private string LocalizeScanStatus(string status) => status.ToLowerInvariant() switch
    {
        "clean" => L["RkhunterStatusClean"],
        "warning" => L["Warning"],
        "running" => L["Running"],
        "failed" => L["Failed"],
        _ => string.Format(L["RkhunterStatusUnknown"], status)
    };
}
