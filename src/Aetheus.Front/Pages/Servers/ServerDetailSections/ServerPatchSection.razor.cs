// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerPatchSection
{
    [Parameter] public int ServerId { get; set; }

    /// <summary>From the server DTO - whether the agent can APPLY updates. Null = unknown (never reported).</summary>
    [Parameter] public bool? PatchManagementAvailable { get; set; }

    [Parameter] public EventCallback OnRefreshRequested { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private ServerSecurityUpdatesDto? _status;
    private bool _loading = true;
    private bool _busy;

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _status = await Api.Security.GetSecurityUpdatesAsync(ServerId);
        }
        catch (HttpRequestException)
        {
            _status = null; // surfaces as "unknown"
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Called by the hosting page when a task completes so a finished upgrade refreshes the counts.</summary>
    public async Task HandleTaskCompletedAsync(TaskCompletedNotification _)
    {
        await LoadAsync();
        StateHasChanged();
    }

    private async Task CheckAsync() => await RunUpgradeAsync(dryRun: true, "PatchDryRunQueued");

    private async Task ApplyAsync()
    {
        var confirmed = await Dialog.Confirm(L["PatchApplyConfirm"].Value, L["PatchApply"].Value,
            new ConfirmOptions { OkButtonText = L["PatchApply"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;
        await RunUpgradeAsync(dryRun: false, "PatchApplyQueued");
    }

    private async Task RunUpgradeAsync(bool dryRun, string successKey)
    {
        _busy = true;
        try
        {
            var taskId = await Api.Security.UpgradeSystemAsync(ServerId, dryRun);
            if (taskId is not null)
                Toast.Success(successKey, successKey);
            else
                Toast.Error("PatchActionFailed", "PatchActionFailed");
        }
        catch (HttpRequestException)
        {
            Toast.Error("PatchActionFailed", "PatchActionFailed");
        }
        finally
        {
            _busy = false;
        }
    }

    // Only the agent with the apt patch-manage grant can apply; unknown (null) is treated as not-yet-capable.
    private bool CanApply => PatchManagementAvailable == true;

    private BadgeStyle SecurityBadgeStyle => (_status?.PendingSecurity ?? 0) > 0 ? BadgeStyle.Danger : BadgeStyle.Light;
}
