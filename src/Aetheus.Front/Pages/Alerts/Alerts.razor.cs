// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Alerts;

public partial class Alerts : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private AlertNotificationService AlertNotify { get; set; } = default!;

    private List<AlertRuleDto> _alerts = [];
    private bool _loading = true;
    private HubConnection? _hubConnection;

    protected override async Task OnInitializedAsync()
    {
        Breadcrumb.Set(new BreadcrumbItem(L["Alerts"]));
        // Re-render when a new fired alert lands so the "Recent alerts" section stays live.
        AlertNotify.OnChange += OnRecentAlertsChanged;
        try { _alerts = await Api.Monitoring.GetAlertRulesAsync(); }
        catch (HttpRequestException) { _alerts = []; }
        _loading = false;
        await ConnectToHub();
    }

    private void OnRecentAlertsChanged() => InvokeAsync(StateHasChanged);

    private static BadgeStyle RecentSeverityBadge(string severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => BadgeStyle.Danger,
        "warning" => BadgeStyle.Warning,
        "info" => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    // Live refresh on two backend events: AlertTriggered (a rule fired) and AlertRuleChanged
    // (another admin created/edited/deleted a rule), so the list stays current without a manual
    // reload. Best-effort - a hub failure degrades silently to static.
    private async Task ConnectToHub()
    {
        try
        {
            _hubConnection = HubFactory.Create("alerts");
            _hubConnection.On<object>("AlertTriggered", _ => InvokeAsync(ReloadFromHubAsync));
            _hubConnection.On<int>("AlertRuleChanged", _ => InvokeAsync(ReloadFromHubAsync));
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinAlertGroup");
                await ReloadFromHubAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinAlertGroup");
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    private async Task ReloadFromHubAsync()
    {
        try { _alerts = await Api.Monitoring.GetAlertRulesAsync(); }
        catch (HttpRequestException) { /* hub-triggered reload - silent on auth failure */ }
        StateHasChanged();
    }

    // X4D8: create/edit run through AlertEditDialog; the page stays a pure list. The dialog closes
    // with `true` on success, which is our cue to reload (the hub event would also reload, but a
    // local reload avoids depending on the round-trip).
    private Task OpenCreateDialogAsync() => OpenEditDialogAsync(null);

    private async Task OpenEditDialogAsync(AlertRuleDto? alert)
    {
        var result = await Dialog.OpenAsync<AlertEditDialog>(
            alert is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["Alert"] = alert },
            new DialogOptions { Width = "720px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });

        if (result is true)
        {
            try { _alerts = await Api.Monitoring.GetAlertRulesAsync(); }
            catch (HttpRequestException) { /* expired JWT - redirect handled by AuthProvider */ }
            StateHasChanged();
        }
    }

    private async Task DeleteAlert(int id)
    {
        var confirmed = await Dialog.Confirm(L["DeleteConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.Monitoring.DeleteAlertRuleAsync(id),
            "Deleted",
            async () => _alerts = await Api.Monitoring.GetAlertRulesAsync(),
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

    private static BadgeStyle GetMetricBadgeStyle(MetricType metric) => metric switch
    {
        MetricType.Cpu => BadgeStyle.Warning,
        MetricType.Memory => BadgeStyle.Info,
        MetricType.Disk => BadgeStyle.Danger,
        MetricType.DiskFree => BadgeStyle.Danger,
        MetricType.BuildCache => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    private static BadgeStyle GetSeverityBadgeStyle(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => BadgeStyle.Danger,
        AlertSeverity.Warning => BadgeStyle.Warning,
        AlertSeverity.Info => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    public async ValueTask DisposeAsync()
    {
        AlertNotify.OnChange -= OnRecentAlertsChanged;
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeaveAlertGroup"); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}
