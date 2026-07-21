// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerFirewallSection
{
    [Parameter] public int ServerId { get; set; }

    /// <summary>From the server DTO - whether the agent can mutate ufw rules. Null = unknown.</summary>
    [Parameter] public bool? FirewallManagementAvailable { get; set; }

    [Parameter] public EventCallback OnRefreshRequested { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private ServerFirewallDto? _firewall;
    private bool _loading = true;
    private bool _busy;
    private int? _loadedServerId;
    private int _loadGeneration;

    private readonly NewRuleModel _rule = new();
    private FirewallRuleAction _ruleAction = FirewallRuleAction.Allow;

    private enum FirewallRuleAction { Allow, Deny }

    private static readonly string[] Protocols = ["tcp", "udp"];

    private sealed class NewRuleModel
    {
        [Range(1, 65535)]
        public int Port { get; set; } = 8080;

        public string Protocol { get; set; } = "tcp";

        [Required]
        [StringLength(64)]
        public string Source { get; set; } = "any";
    }

    private bool CanManage => FirewallManagementAvailable == true;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var serverId = ServerId;
        var generation = Interlocked.Increment(ref _loadGeneration);
        _loading = true;
        try
        {
            var firewall = await Api.GetFirewallAsync(serverId);
            if (serverId == ServerId && generation == _loadGeneration)
                _firewall = firewall;
        }
        catch (HttpRequestException)
        {
            if (serverId == ServerId && generation == _loadGeneration)
                _firewall = null;
        }
        finally
        {
            if (serverId == ServerId && generation == _loadGeneration)
                _loading = false;
        }
    }

    public async Task HandleTaskCompletedAsync(TaskCompletedNotification _)
    {
        await LoadAsync();
        StateHasChanged();
    }

    private Task SubmitRuleAsync(NewRuleModel _) => _ruleAction == FirewallRuleAction.Allow
        ? RunAsync(() => Api.FirewallAllowAsync(ServerId, BuildRequest()), "FirewallRuleQueued")
        : RunAsync(() => Api.FirewallDenyAsync(ServerId, BuildRequest()), "FirewallRuleQueued");

    private string LocalizeAction(string action) => action.ToLowerInvariant() switch
    {
        "allow" => L["FirewallAllow"],
        "deny" => L["FirewallDeny"],
        _ => string.Format(L["FirewallUnknownAction"], action)
    };

    private async Task ToggleAsync(bool enable)
    {
        if (!enable)
        {
            var confirmed = await Dialog.Confirm(L["FirewallDisableConfirm"].Value, L["FirewallToggle"].Value,
                new ConfirmOptions { OkButtonText = L["FirewallDisable"].Value, CancelButtonText = L["Cancel"].Value });
            if (confirmed != true) return;
        }
        await RunAsync(() => Api.FirewallToggleAsync(ServerId, enable), enable ? "FirewallEnableQueued" : "FirewallDisableQueued");
    }

    private async Task DeleteRuleAsync(FirewallRuleDto rule)
    {
        if (rule.Protocol is not ("tcp" or "udp"))
        {
            Toast.Error("FirewallActionFailed", "FirewallActionFailed");
            return;
        }

        var confirmed = await Dialog.Confirm(
            string.Format(L["FirewallDeleteConfirm"].Value, rule.Raw), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await RunAsync(() => Api.FirewallDeleteRuleAsync(ServerId, new FirewallRuleRequest
        {
            Port = rule.Port ?? 0,
            Protocol = rule.Protocol,
            Source = string.Equals(rule.Source, "Anywhere", StringComparison.OrdinalIgnoreCase) ? "any" : rule.Source
        }), "FirewallRuleQueued");
    }

    private FirewallRuleRequest BuildRequest() => new()
    {
        Port = _rule.Port,
        Protocol = _rule.Protocol,
        Source = string.IsNullOrWhiteSpace(_rule.Source) ? "any" : _rule.Source.Trim()
    };

    private async Task RunAsync(Func<Task<int?>> action, string successKey)
    {
        _busy = true;
        try
        {
            var taskId = await action();
            if (taskId is not null)
                Toast.Success(successKey, successKey);
            else
                Toast.Error("FirewallActionFailed", "FirewallActionFailed");
        }
        catch (HttpRequestException)
        {
            Toast.Error("FirewallActionFailed", "FirewallActionFailed");
        }
        finally
        {
            _busy = false;
        }
    }

    // Anti-lockout affordance: never offer to delete the SSH admin port from the UI (the backend + helper
    // refuse it anyway, but hiding the button avoids a confusing rejection).
    private static bool IsDeletable(FirewallRuleDto rule)
        => rule.Port is > 0 and not 22;
}
