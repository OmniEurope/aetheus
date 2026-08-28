// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Notifications;

public partial class NotificationsAdmin : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    private List<NotificationChannelDto> _channels = [];
    private List<NotificationRuleDto> _rules = [];
    private List<NotificationChannelDto> _dialogChannels = [];
    private Aetheus.Front.Shared.AetheusDataGrid<NotificationChannelDto>? _channelsGrid;
    private Aetheus.Front.Shared.AetheusDataGrid<NotificationRuleDto>? _rulesGrid;
    private int _channelTotalCount;
    private int _ruleTotalCount;
    private string _channelSearch = string.Empty;
    private string _ruleSearch = string.Empty;
    private bool _channelsLoading;
    private bool _rulesLoading;
    private readonly HashSet<int> _testing = [];
    private readonly Dictionary<int, NotificationTestResultDto> _channelTest = [];

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }
        Breadcrumb.Set(new BreadcrumbItem(L["Administration"], "/admin"), new BreadcrumbItem(L["NotificationRules"]));
        await Task.CompletedTask;
    }

    private async Task LoadChannelsAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args, "Name");
        _channelsLoading = true;
        try
        {
            var result = await Api.Monitoring.GetNotificationChannelsPagedAsync(
                page, pageSize, _channelSearch, sortBy, sortDescending);
            _channels = result.Items;
            _channelTotalCount = result.TotalCount;
        }
        catch (HttpRequestException) { /* 401 redirect handled upstream */ }
        finally { _channelsLoading = false; }
    }

    private async Task LoadRulesAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args, "EventType");
        _rulesLoading = true;
        try
        {
            var result = await Api.Monitoring.GetNotificationRulesPagedAsync(
                page, pageSize, _ruleSearch, sortBy, sortDescending);
            _rules = result.Items;
            _ruleTotalCount = result.TotalCount;
        }
        catch (HttpRequestException) { /* 401 redirect handled upstream */ }
        finally { _rulesLoading = false; }
    }

    private async Task ReloadAllAsync()
    {
        var channelReload = _channelsGrid?.Reload() ?? Task.CompletedTask;
        var ruleReload = _rulesGrid?.Reload() ?? Task.CompletedTask;
        await Task.WhenAll(channelReload, ruleReload);
    }

    private Task ResetChannelSearchAsync() => _channelsGrid?.GoToPage(0) ?? Task.CompletedTask;
    private Task ResetRuleSearchAsync() => _rulesGrid?.GoToPage(0) ?? Task.CompletedTask;

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args, string fallback)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (fallback, false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 &&
            parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private async Task OpenChannelDialogAsync(NotificationChannelDto? channel)
    {
        var result = await Dialog.OpenAsync<NotificationChannelEditDialog>(
            channel is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["ChannelId"] = channel?.Id },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        if (result is true)
        {
            _dialogChannels = [];
            await ReloadAllAsync();
        }
    }

    private async Task OpenRuleDialogAsync(NotificationRuleDto? rule)
    {
        if (_dialogChannels.Count == 0)
            _dialogChannels = await Api.Monitoring.GetNotificationChannelsAsync();
        var result = await Dialog.OpenAsync<NotificationRuleEditDialog>(
            rule is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["Rule"] = rule, ["Channels"] = _dialogChannels },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        if (result is true && _rulesGrid is not null) await _rulesGrid.Reload();
    }

    private async Task TestChannelAsync(int id)
    {
        _testing.Add(id);
        _channelTest.Remove(id);
        StateHasChanged();
        try
        {
            var result = await Api.Monitoring.TestNotificationChannelAsync(id);
            _channelTest[id] = result ?? new NotificationTestResultDto
            {
                Status = NotificationTestStatus.Failed,
                Message = L["NotificationChannelNotFound"]
            };
            var testResult = _channelTest[id];
            Notify.Notify(
                testResult.Status == NotificationTestStatus.Sent
                    ? NotificationSeverity.Success
                    : NotificationSeverity.Error,
                testResult.Status == NotificationTestStatus.Sent ? "Saved" : "Error",
                testResult.Message ?? L["NotificationChannelTestFailed"]);
        }
        catch (HttpRequestException)
        {
            _channelTest[id] = new NotificationTestResultDto { Status = NotificationTestStatus.Failed, Message = L["NotificationChannelTestFailed"] };
            Notify.Error("Error", "NotificationChannelTestFailed");
        }
        finally
        {
            _testing.Remove(id);
            StateHasChanged();
        }
    }

    private async Task DeleteChannelAsync(NotificationChannelDto channel)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteNotificationChannelConfirm"], channel.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.Monitoring.DeleteNotificationChannelAsync(channel.Id),
            "Deleted",
            async () =>
            {
                _channelTest.Remove(channel.Id);
                _dialogChannels = [];
                await ReloadAllAsync();
            },
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

    private async Task DeleteRuleAsync(NotificationRuleDto rule)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteNotificationRuleConfirm"], rule.EventType), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.Monitoring.DeleteNotificationRuleAsync(rule.Id),
            "Deleted",
            () => _rulesGrid?.Reload() ?? Task.CompletedTask,
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

    private static BadgeStyle TestBadge(NotificationTestStatus status) => status switch
    {
        NotificationTestStatus.Sent => BadgeStyle.Success,
        NotificationTestStatus.NotConfigured => BadgeStyle.Light,
        _ => BadgeStyle.Warning
    };
}
