// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerApacheSection : IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private bool _actionRunning;
    private string _moduleSearch = string.Empty;
    private string _vhostSearch = string.Empty;

    // Logs
    private string _logType = "error";
    private int _logLines = 100;
    private string? _logContent;
    private bool _logLoading;
    private bool _logFollowing;
    private System.Threading.Timer? _followTimer;
    private System.Threading.Timer? _configTimeoutTimer;

    // Config editor - the form now lives in ApacheConfigEditorDialog (DialogService). The
    // shared model bridges the SignalR-pushed config text (HandleTaskCompleted) into the open
    // dialog. Non-null while a config-editor dialog is open so SignalR pushes can reach it.
    private ApacheConfigEditorModel? _configEditor;

    // Confirmation dialog
    private bool _confirmVisible;
    private string _confirmTitle = string.Empty;
    private string _confirmMessage = string.Empty;
    private Func<Task>? _confirmAction;


    private ApacheDataDto Apache => Server.Apache;

    private List<ApacheModuleDto> FilteredModules => string.IsNullOrWhiteSpace(_moduleSearch)
        ? Apache.Modules
        : Apache.Modules.Where(m => m.Name.Contains(_moduleSearch, StringComparison.OrdinalIgnoreCase)).ToList();

    private List<ApacheVirtualHostDto> FilteredVirtualHosts => string.IsNullOrWhiteSpace(_vhostSearch)
        ? Apache.VirtualHosts
        : Apache.VirtualHosts.Where(v =>
            v.ServerName.Contains(_vhostSearch, StringComparison.OrdinalIgnoreCase) ||
            v.DocumentRoot.Contains(_vhostSearch, StringComparison.OrdinalIgnoreCase)).ToList();

    private List<ApacheVirtualHostDto> EnabledVirtualHosts => FilteredVirtualHosts.Where(v => v.IsEnabled).ToList();
    private List<ApacheVirtualHostDto> DisabledVirtualHosts => FilteredVirtualHosts.Where(v => !v.IsEnabled).ToList();

    // Item #5.1: group vhosts by ServerName so a domain with separate :80 and :443 entries
    // surfaces as ONE row with two port chips, instead of two duplicate-looking rows. The
    // grouping is case-insensitive (Apache itself is case-insensitive on ServerName).
    internal List<ApacheVirtualHostGroup> EnabledVirtualHostGroups => GroupBy(EnabledVirtualHosts);
    internal List<ApacheVirtualHostGroup> DisabledVirtualHostGroups => GroupBy(DisabledVirtualHosts);

    private static List<ApacheVirtualHostGroup> GroupBy(IEnumerable<ApacheVirtualHostDto> source) =>
        source
            .GroupBy(v => string.IsNullOrEmpty(v.ServerName) ? v.ConfigFile : v.ServerName,
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => new ApacheVirtualHostGroup(
                ServerName: g.First().ServerName,
                Members: g.OrderBy(v => v.Port).ToList()))
            .OrderBy(g => g.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Synthetic row for the grouped sites table. Members are ordered by port asc so
    /// the chips render predictably (80 then 443 then anything else).</summary>
    internal sealed record ApacheVirtualHostGroup(string ServerName, List<ApacheVirtualHostDto> Members)
    {
        /// <summary>Primary config file - picked as the lowest-port member's. The two members
        /// of a domain usually share a single .conf, so editing the primary is the common case;
        /// when they diverge, the UI can offer a per-port action.</summary>
        public ApacheVirtualHostDto Primary => Members[0];

        /// <summary>Comma-separated DocumentRoot list when members disagree, else the single value.</summary>
        public string DocumentRootsCompact =>
            string.Join(", ",
                Members.Select(m => m.DocumentRoot)
                       .Where(d => !string.IsNullOrWhiteSpace(d))
                       .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private List<string> ApacheResourceNames => Apache.VirtualHosts.Select(v => v.ServerName).ToList();

    private async Task ExecuteActionAsync(ApacheAction action, string? targetName = null)
    {
        _actionRunning = true;
        try
        {
            var success = await Api.ExecuteApacheActionAsync(ServerId, new ApacheActionRequest
            {
                Action = action,
                TargetName = targetName
            });
            if (success)
                Toast.Success(L["TaskQueued"]);
            else
                Toast.Error(L["ActionFailed"]);
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private async Task FetchLogsAsync()
    {
        _logLoading = true;
        _logContent = null;
        try
        {
            var success = await Api.GetApacheLogsAsync(ServerId, new ApacheLogRequest
            {
                LogType = _logType,
                Lines = _logLines
            });
            if (success)
                _logContent = L["TaskQueued"];
            else
                Toast.Error(L["ActionFailed"]);
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _logLoading = false;
        }
    }

    private async Task OpenConfigEditorAsync(string siteName)
    {
        var model = new ApacheConfigEditorModel { SiteName = siteName, Loading = true };
        _configEditor = model;

        // The actual config text is fetched on the agent and pushed back over SignalR
        // (see HandleTaskCompleted, which writes into the shared model and raises OnChanged).
        // The API call below only queues that task, so the dialog shows a loading indicator
        // until the content lands. If the queue request fails, stop the spinner and surface
        // the error rather than leaving the editor empty.
        try
        {
            var status = await Api.GetApacheVHostConfigAsync(ServerId, siteName);
            if (!status.Success)
            {
                model.Loading = false;
                model.NotifyChanged();
                Toast.Error(L["ActionFailed"]);
            }
            else
            {
                StartConfigTimeout(model);
            }
        }
        catch
        {
            model.Loading = false;
            model.NotifyChanged();
            Toast.Error(L["ActionFailed"]);
        }

        // Open the dialog (un-awaited here only conceptually - OpenAsync awaits the user's
        // Save/Cancel). The dialog returns an ApacheVHostSaveRequest on Save.
        var result = await Dialog.OpenAsync<ApacheConfigEditorDialog>(
            $"{L["EditConfig"]} - {siteName}",
            new Dictionary<string, object?> { { "Model", model } },
            new DialogOptions { Width = "50rem", CloseDialogOnOverlayClick = true });

        CancelConfigTimeout();
        if (ReferenceEquals(_configEditor, model))
            _configEditor = null;

        if (result is ApacheVHostSaveRequest request)
            await SaveConfigAsync(request);
    }

    // S-UX-40: the config text is pushed back over SignalR; if the agent never answers, clear the
    // spinner after 30s and surface an error instead of leaving the editor spinning forever.
    private void StartConfigTimeout(ApacheConfigEditorModel model)
    {
        _configTimeoutTimer?.Dispose();
        _configTimeoutTimer = new System.Threading.Timer(
            _ => _ = InvokeAsync(() =>
            {
                if (!model.Loading) return;
                model.Loading = false;
                model.NotifyChanged();
                Toast.Error(L["ActionFailed"]);
            }),
            null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    private void CancelConfigTimeout()
    {
        _configTimeoutTimer?.Dispose();
        _configTimeoutTimer = null;
    }

    private async Task SaveConfigAsync(ApacheVHostSaveRequest request)
    {
        try
        {
            var success = await Api.SaveApacheVHostConfigAsync(ServerId, request);
            if (success)
                Toast.Success(L["ConfigSaved"]);
            else
                Toast.Error(L["ActionFailed"]);
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
    }

    private void ShowConfirm(string title, string message, Func<Task> action)
    {
        _confirmTitle = title;
        _confirmMessage = message;
        _confirmAction = action;
        _confirmVisible = true;
    }

    private async Task ConfirmAccepted()
    {
        _confirmVisible = false;
        if (_confirmAction is not null)
            await _confirmAction();
    }

    private void ConfirmCancelled() => _confirmVisible = false;

    public void HandleTaskCompleted(TaskCompletedNotification notification)
    {
        if (notification.TaskName.Contains("Apache logs", StringComparison.OrdinalIgnoreCase))
        {
            _logContent = notification.Output;
            _logLoading = false;
            _ = InvokeAsync(StateHasChanged);
        }
        else if (notification.TaskName.Contains("Apache config", StringComparison.OrdinalIgnoreCase)
                 && !notification.TaskName.Contains("save", StringComparison.OrdinalIgnoreCase)
                 && _configEditor is { } model)
        {
            model.Content = notification.Output ?? string.Empty;
            model.Loading = false;
            CancelConfigTimeout();
            _ = InvokeAsync(model.NotifyChanged);
        }
    }

    internal static BadgeStyle GetStatusBadge(bool isRunning) =>
        isRunning ? BadgeStyle.Success : BadgeStyle.Danger;

    private void ToggleFollow()
    {
        _logFollowing = !_logFollowing;
        if (_logFollowing)
        {
            _followTimer = new System.Threading.Timer(
                async _ => await InvokeAsync(async () =>
                {
                    if (!_logLoading)
                        await FetchLogsAsync();
                }),
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
        }
        else
        {
            StopFollow();
        }
    }

    private void StopFollow()
    {
        _followTimer?.Dispose();
        _followTimer = null;
        _logFollowing = false;
    }

    public void Dispose()
    {
        _followTimer?.Dispose();
        _configTimeoutTimer?.Dispose();
    }
}
