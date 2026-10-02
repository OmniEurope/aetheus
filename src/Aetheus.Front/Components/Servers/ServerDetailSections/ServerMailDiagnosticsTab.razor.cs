// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>
/// PLAN-005 lots 6 and 7: diagnostics without SSH. Backend checklist (services, TLS, DNS, reverse DNS,
/// MX, helper version), server-side configuration check (<c>CHECK</c> lines), end-to-end delivery test
/// (<c>DELIVERY</c> line) and mailbox usage measurement, each read from the task it queued.
/// </summary>
public partial class ServerMailDiagnosticsTab : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private ILogger<ServerMailDiagnosticsTab> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter] public bool CanManage { get; set; }
    [Parameter] public string DefaultSender { get; set; } = string.Empty;

    /// <summary>Raised once measured mailbox usage has been stored, so the accounts grid reloads.</summary>
    [Parameter] public EventCallback OnUsageMeasured { get; set; }

    private MailDiagnosticsDto? _diagnostics;
    private bool _loading;
    private int? _checkTaskId;
    private int? _deliveryTaskId;
    private int? _quotaTaskId;
    private MailDeliveryResultDto? _delivery;
    private List<MailComponentCheckDto> _checks = [];

    internal MailDeliveryResultDto? Delivery => _delivery;
    internal IReadOnlyList<MailComponentCheckDto> Checks => _checks;

    // R-181: replaces the Refresh button. Reading the checklist queues no agent task (inventory + DNS),
    // so completed tasks can trigger a reload as safely as heartbeats.
    private ServerLiveFeed? _liveFeed;
    internal ServerLiveFeed? LiveFeed => _liveFeed;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await LoadDiagnosticsAsync();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail diagnostics load failed for server {ServerId}", ServerId);
            _loading = false;
        }
    }

    protected override void OnParametersSet()
    {
        _liveFeed ??= new ServerLiveFeed(HubFactory);
        if (_liveFeed.ServerId != ServerId)
            _ = _liveFeed.StartAsync(ServerId, ServerLiveFeedTriggers.HeartbeatAndTasks,
                () => InvokeAsync(ReloadFromPushAsync));
    }

    private async Task ReloadFromPushAsync()
    {
        try
        {
            await LoadDiagnosticsAsync();
        }
        catch (HttpRequestException ex)
        {
            // The last checklist stays on screen; the next push retries.
            Logger.LogWarning(ex, "Mail diagnostics reload failed for server {ServerId}", ServerId);
        }
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_liveFeed is not null)
            await _liveFeed.DisposeAsync();
    }

    private async Task LoadDiagnosticsAsync()
    {
        _loading = true;
        try
        {
            _diagnostics = await Api.Mail.GetMailDiagnosticsAsync(ServerId);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RunServerCheckAsync()
    {
        _checks = [];
        var queued = await Api.Mail.QueueMailActionAsync(ServerId, new MailActionRequest { Action = MailAction.TestConfig });
        if (queued is null)
        {
            Toast.Error(L["ActionFailed"]);
            return;
        }
        _checkTaskId = queued.TaskId;
        Toast.Info("TaskQueued", "MailWaitingForTask");
    }

    private async Task SendTestAsync()
    {
        var result = await Dialog.OpenAsync<MailOperationDialog>(L["MailSendTest"],
            new Dictionary<string, object?>
            {
                { "Mode", MailDialogMode.TestDelivery },
                { "Model", new MailDialogModel { Source = DefaultSender } }
            },
            new OmniDialogOptions { Width = "32rem", AutoFocusFirstElement = false });
        if (result is not MailDialogModel model) return;

        _delivery = null;
        var queued = await Api.Mail.SendMailTestAsync(ServerId, new MailTestDeliveryRequest { From = model.Source, To = model.Destination });
        if (queued is null)
        {
            Toast.Error(L["ActionFailed"]);
            return;
        }
        _deliveryTaskId = queued.TaskId;
        Toast.Info("TaskQueued", "MailWaitingForTask");
    }

    private async Task MeasureUsageAsync()
    {
        var queued = await Api.Mail.RefreshMailQuotaAsync(ServerId);
        if (queued is null)
        {
            Toast.Error(L["ActionFailed"]);
            return;
        }
        _quotaTaskId = queued.TaskId;
        Toast.Info("TaskQueued", "MailWaitingForTask");
    }

    /// <summary>Called by the mail section for every completed task of the server.</summary>
    public async Task HandleTaskCompletedAsync(TaskCompletedNotification notification)
    {
        if (notification.TaskId == _checkTaskId)
        {
            _checkTaskId = null;
            _checks = MailTaskOutputParser.ParseChecks(await MailTaskOutputLoader.ReadAsync(Api, notification.TaskId));
            if (_checks.Count == 0) Toast.Error(L["ActionFailed"]);
        }
        else if (notification.TaskId == _deliveryTaskId)
        {
            _deliveryTaskId = null;
            // The helper exits non-zero for deferred/bounced mail but still prints the DELIVERY line.
            _delivery = MailTaskOutputParser.ParseDelivery(await MailTaskOutputLoader.ReadAsync(Api, notification.TaskId))
                ?? new MailDeliveryResultDto { Status = notification.Status.ToString(), Detail = L["ActionFailed"] };
        }
        else if (notification.TaskId == _quotaTaskId)
        {
            _quotaTaskId = null;
            if (notification.Status == TaskExecutionStatus.Success)
            {
                var updated = await Api.Mail.IngestMailQuotaAsync(ServerId, notification.TaskId);
                Toast.Success("MailMeasureUsage", "MailUsageUpdated", updated);
                await OnUsageMeasured.InvokeAsync();
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        else
        {
            return;
        }
        await InvokeAsync(StateHasChanged);
    }
}
