// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerTeamspeakSection : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private bool _actionRunning;
    private TeamspeakDataCoordinator _data = default!;
    private int? _loadedServerId;

    private string _setupInstallPath = "/opt/teamspeak3-server_linux_amd64";
    private int _setupVoicePort = 9987;
    private int _setupQueryPort = 10011;

    private int _kickClientId;
    private string _kickNickname = string.Empty;
    private string _kickReason = string.Empty;

    private int _moveClientId;
    private string _moveNickname = string.Empty;
    private int _moveTargetChannelId;
    private string _moveChannelPassword = string.Empty;

    private int _pokeClientId;
    private string _pokeNickname = string.Empty;
    private string _pokeMessage = string.Empty;

    private int _gracefulRestartSeconds = 60;
    private string _gracefulRestartMessage = string.Empty;

    private string _banClientUniqueId = string.Empty;
    private string _banNickname = string.Empty;
    private string _banReason = string.Empty;
    private int _banDuration = 3600;

    private string _newChannelName = string.Empty;
    private int? _newChannelParentId;
    private string _newChannelPassword = string.Empty;
    private int? _newChannelMaxClients;
    private bool _newChannelPermanent = true;

    private int _editChannelId;
    private string _editChannelName = string.Empty;
    private string _editChannelPassword = string.Empty;
    private int? _editChannelMaxClients;

    private string _snapshotBlob = string.Empty;
    private string _snapshotConfirmation = string.Empty;

    private string _editServerName = string.Empty;
    private string _editServerPassword = string.Empty;
    private int? _editServerMaxClients;
    private string _editWelcomeMessage = string.Empty;

    private string _globalMessage = string.Empty;

    // Log lines
    private int _logLines = 100;

    private TeamspeakDataDto Ts => _data?.State ?? Server.Teamspeak;

    protected override void OnInitialized()
    {
        _data = new TeamspeakDataCoordinator(Api);
        _gracefulRestartMessage = L["TeamspeakGracefulRestartMessage"];
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        ResetOperationTargets();
        await _data.ResetAsync(ServerId, Server.Teamspeak, () => InvokeAsync(StateHasChanged));
    }

    public async Task HandleTaskCompletedAsync(TaskCompletedNotification notification)
    {
        if (notification.ServerId != ServerId ||
            !notification.TaskName.StartsWith("TeamSpeak", StringComparison.OrdinalIgnoreCase))
            return;
        await _data.RefreshAllAsync();
    }

    public ValueTask DisposeAsync() => _data.DisposeAsync();

    private void ResetOperationTargets()
    {
        _actionRunning = false;
        _kickClientId = 0;
        _kickNickname = string.Empty;
        _kickReason = string.Empty;
        _moveClientId = 0;
        _moveNickname = string.Empty;
        _moveTargetChannelId = 0;
        _moveChannelPassword = string.Empty;
        _pokeClientId = 0;
        _pokeNickname = string.Empty;
        _pokeMessage = string.Empty;
        _banClientUniqueId = string.Empty;
        _banNickname = string.Empty;
        _banReason = string.Empty;
        _banDuration = 3600;
        _newChannelName = string.Empty;
        _newChannelParentId = null;
        _newChannelPassword = string.Empty;
        _newChannelMaxClients = null;
        _editChannelId = 0;
        _editChannelName = string.Empty;
        _editChannelPassword = string.Empty;
        _editChannelMaxClients = null;
        _snapshotBlob = string.Empty;
        _snapshotConfirmation = string.Empty;
        _editServerName = string.Empty;
        _editServerPassword = string.Empty;
        _editServerMaxClients = null;
        _editWelcomeMessage = string.Empty;
        _globalMessage = string.Empty;
    }

    private async Task<bool> RunOperationAsync(
        Func<Task<ApiStatus>> operation, string label, Action? onSuccess = null)
    {
        try
        {
            var status = await operation();
            if (!status.Success)
            {
                Toast.Error("Error", "OperationFailed");
                return false;
            }

            onSuccess?.Invoke();
            Toast.Notify(NotificationSeverity.Success, "TaskCreated", label);
            return true;
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "OperationFailed");
            return false;
        }
    }

    private async Task ExecuteActionAsync(TeamspeakAction action)
    {
        _actionRunning = true;
        try
        {
            await RunOperationAsync(
                () => Api.ExecuteTeamspeakActionAsync(ServerId, new TeamspeakActionRequest { Action = action }),
                action.ToString());
        }
        finally { _actionRunning = false; }
    }

    private async Task<TeamspeakDialogModel?> OpenDialogAsync(TeamspeakDialogMode mode, string title, TeamspeakDialogModel model, string width = "32rem")
    {
        var channels = mode is TeamspeakDialogMode.Move or TeamspeakDialogMode.CreateChannel
            ? await _data.LoadAllChannelsAsync()
            : _data.Channels;
        var result = await Dialog.OpenAsync<TeamspeakOperationDialog>(title,
            new Dictionary<string, object?>
            {
                { "Mode", mode }, { "Model", model }, { "Channels", channels }, { "ServerName", Ts.ServerName }
            },
            new DialogOptions { Width = width });
        return result as TeamspeakDialogModel;
    }

    private async Task OpenSetupDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Setup, L["TeamspeakSetup"], new TeamspeakDialogModel
        {
            InstallPath = _setupInstallPath,
            VoicePort = _setupVoicePort,
            QueryPort = _setupQueryPort
        });
        if (result is null) return;
        _setupInstallPath = result.InstallPath;
        _setupVoicePort = result.VoicePort;
        _setupQueryPort = result.QueryPort;
        await SetupAsync();
    }

    private async Task OpenGracefulRestartDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.GracefulRestart, L["GracefulRestart"], new TeamspeakDialogModel
        {
            WarningSeconds = _gracefulRestartSeconds,
            Message = _gracefulRestartMessage
        });
        if (result is null) return;
        _gracefulRestartSeconds = result.WarningSeconds;
        _gracefulRestartMessage = result.Message;
        await GracefulRestartAsync();
    }

    private async Task OpenSnapshotDeployDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.SnapshotDeploy, L["SnapshotDeploy"], new TeamspeakDialogModel(), "38rem");
        if (result is null) return;
        _snapshotBlob = result.SnapshotBlob;
        _snapshotConfirmation = result.Confirmation;
        await DeploySnapshotAsync();
    }

    private async Task OpenCreateChannelDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.CreateChannel, L["CreateChannel"], new TeamspeakDialogModel { IsPermanent = true });
        if (result is null) return;
        _newChannelName = result.Name;
        _newChannelParentId = result.ParentId;
        _newChannelPassword = result.Password;
        _newChannelMaxClients = result.MaxClients;
        _newChannelPermanent = result.IsPermanent;
        await CreateChannelAsync();
    }

    private async Task OpenEditServerDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.EditServer, L["EditServer"], new TeamspeakDialogModel
        {
            Name = Ts.ServerName,
            MaxClients = Ts.MaxClients > 0 ? Ts.MaxClients : null
        });
        if (result is null) return;
        _editServerName = result.Name;
        _editServerPassword = result.Password;
        _editServerMaxClients = result.MaxClients;
        _editWelcomeMessage = result.Message;
        await EditServerAsync();
    }

    private async Task OpenMessageDialogAsync()
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Message, L["SendMessage"], new TeamspeakDialogModel());
        if (result is null) return;
        _globalMessage = result.Message;
        await SendMessageAsync();
    }

    private async Task SetupAsync()
    {
        await RunOperationAsync(
            () => Api.SetupTeamspeakAsync(ServerId, new TeamspeakSetupRequest
            {
                InstallPath = _setupInstallPath,
                VoicePort = _setupVoicePort,
                QueryPort = _setupQueryPort
            }),
            L["TeamspeakSetup"]);
    }

    private async Task ShowKickDialog(TeamspeakClientDto client)
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Kick, L["KickClient"], new TeamspeakDialogModel { ClientId = client.ClientId, Nickname = client.Nickname });
        if (result is null) return;
        _kickClientId = result.ClientId;
        _kickNickname = result.Nickname;
        _kickReason = result.Reason;
        await KickAsync();
    }

    private async Task KickAsync()
    {
        await RunOperationAsync(
            () => Api.KickTeamspeakClientAsync(ServerId, new TeamspeakKickRequest
            {
                ClientId = _kickClientId,
                ReasonMessage = _kickReason
            }),
            L["KickClient"]);
    }

    // Item #9 tier-1: relocate a client. Default target = client's current channel (so the
    // dropdown can show ALL channels including the current one and skip the no-op silently).
    private async Task ShowMoveDialog(TeamspeakClientDto client)
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Move, L["MoveClient"], new TeamspeakDialogModel { ClientId = client.ClientId, Nickname = client.Nickname, TargetChannelId = client.ChannelId });
        if (result is null) return;
        _moveClientId = result.ClientId;
        _moveNickname = result.Nickname;
        _moveTargetChannelId = result.TargetChannelId;
        _moveChannelPassword = result.Password;
        await MoveAsync();
    }

    private async Task MoveAsync()
    {
        await RunOperationAsync(
            () => Api.MoveTeamspeakClientAsync(ServerId, new TeamspeakMoveClientRequest
            {
                ClientId = _moveClientId,
                TargetChannelId = _moveTargetChannelId,
                ChannelPassword = string.IsNullOrEmpty(_moveChannelPassword) ? null : _moveChannelPassword
            }),
            L["MoveClient"]);
    }

    // Item #9 tier-1: send a single-client popup (poke). Distinct from the server-wide
    // "Send message" which broadcasts to every connected client.
    private async Task ShowPokeDialog(TeamspeakClientDto client)
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Poke, L["PokeClient"], new TeamspeakDialogModel { ClientId = client.ClientId, Nickname = client.Nickname });
        if (result is null) return;
        _pokeClientId = result.ClientId;
        _pokeNickname = result.Nickname;
        _pokeMessage = result.Message;
        await PokeAsync();
    }

    private async Task PokeAsync()
    {
        if (string.IsNullOrWhiteSpace(_pokeMessage)) return;
        await RunOperationAsync(
            () => Api.PokeTeamspeakClientAsync(ServerId, new TeamspeakPokeClientRequest
            {
                ClientId = _pokeClientId,
                Message = _pokeMessage
            }),
            L["PokeClient"]);
    }

    // ===== Item #9 tier-2/3 - fire-and-forget ops that surface their result in the task tracker. =====

    private async Task FetchClientInfoAsync(TeamspeakClientDto client)
    {
        await RunOperationAsync(
            () => Api.GetTeamspeakClientInfoAsync(
                ServerId, new TeamspeakClientInfoRequest { ClientId = client.ClientId }),
            $"{L["ClientInfo"]} - {client.Nickname}");
    }

    private async Task GracefulRestartAsync()
    {
        await RunOperationAsync(
            () => Api.TeamspeakGracefulRestartAsync(ServerId, new TeamspeakGracefulRestartRequest
            {
                WarningSeconds = _gracefulRestartSeconds,
                WarningMessage = _gracefulRestartMessage
            }),
            L["GracefulRestart"]);
    }

    private async Task FetchServerInfoAsync()
    {
        await RunOperationAsync(
            () => Api.GetTeamspeakServerInfoAsync(ServerId),
            L["TeamspeakStats"]);
    }

    private async Task CreateSnapshotAsync()
    {
        await RunOperationAsync(
            () => Api.CreateTeamspeakSnapshotAsync(ServerId, new TeamspeakSnapshotCreateRequest()),
            L["SnapshotCreate"]);
    }

    private async Task DeploySnapshotAsync()
    {
        await RunOperationAsync(
            () => Api.DeployTeamspeakSnapshotAsync(ServerId, new TeamspeakSnapshotDeployRequest
            {
                SnapshotBlob = _snapshotBlob,
                Confirmation = _snapshotConfirmation
            }),
            L["SnapshotDeploy"],
            () =>
            {
                _snapshotBlob = string.Empty;
                _snapshotConfirmation = string.Empty;
            });
    }

    private async Task ListServerGroupsAsync()
    {
        await RunOperationAsync(
            () => Api.ListTeamspeakServerGroupsAsync(ServerId),
            L["TeamspeakServerGroups"]);
    }

    private async Task ListTokensAsync()
    {
        await RunOperationAsync(
            () => Api.ListTeamspeakTokensAsync(ServerId),
            L["TeamspeakTokens"]);
    }

    private async Task ListComplaintsAsync()
    {
        await RunOperationAsync(
            () => Api.ListTeamspeakComplaintsAsync(ServerId),
            L["TeamspeakComplaints"]);
    }

    private async Task ShowBanDialog(TeamspeakClientDto client)
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.Ban, L["BanClient"], new TeamspeakDialogModel { ClientUniqueId = client.UniqueId, Nickname = client.Nickname, Duration = 3600 });
        if (result is null) return;
        _banClientUniqueId = result.ClientUniqueId;
        _banNickname = result.Nickname;
        _banReason = result.Reason;
        _banDuration = result.Duration;
        await BanAsync();
    }

    private async Task BanAsync()
    {
        await RunOperationAsync(
            () => Api.BanTeamspeakClientAsync(ServerId, new TeamspeakBanRequest
            {
                ClientUniqueId = _banClientUniqueId,
                DurationSeconds = _banDuration,
                Reason = _banReason
            }),
            L["BanClient"]);
    }

    private async Task LoadLogsAsync()
    {
        await RunOperationAsync(
            () => Api.GetTeamspeakLogsAsync(ServerId, new TeamspeakLogRequest { Lines = _logLines }),
            L["Logs"]);
    }

    private async Task CreateChannelAsync()
    {
        await RunOperationAsync(
            () => Api.CreateTeamspeakChannelAsync(ServerId, new TeamspeakCreateChannelRequest
            {
                Name = _newChannelName,
                ParentId = _newChannelParentId,
                Password = string.IsNullOrWhiteSpace(_newChannelPassword) ? null : _newChannelPassword,
                MaxClients = _newChannelMaxClients,
                IsPermanent = _newChannelPermanent
            }),
            L["CreateChannel"],
            () =>
            {
                _newChannelName = string.Empty;
                _newChannelPassword = string.Empty;
                _newChannelParentId = null;
                _newChannelMaxClients = null;
            });
    }

    private async Task ShowEditChannelDialog(TeamspeakChannelDto ch)
    {
        var result = await OpenDialogAsync(TeamspeakDialogMode.EditChannel, L["EditChannel"], new TeamspeakDialogModel { ChannelId = ch.Id, Name = ch.Name, MaxClients = ch.MaxClients == -1 ? null : ch.MaxClients });
        if (result is null) return;
        _editChannelId = result.ChannelId;
        _editChannelName = result.Name;
        _editChannelPassword = result.Password;
        _editChannelMaxClients = result.MaxClients;
        await EditChannelAsync();
    }

    private async Task EditChannelAsync()
    {
        await RunOperationAsync(
            () => Api.EditTeamspeakChannelAsync(ServerId, _editChannelId, new TeamspeakEditChannelRequest
            {
                ChannelId = _editChannelId,
                Name = _editChannelName,
                Password = string.IsNullOrWhiteSpace(_editChannelPassword) ? null : _editChannelPassword,
                MaxClients = _editChannelMaxClients
            }),
            L["EditChannel"]);
    }

    private async Task ShowDeleteChannelConfirm(TeamspeakChannelDto ch)
    {
        var confirmed = await Dialog.Confirm(string.Format(L["ConfirmDeleteChannel"], ch.Name), L["DeleteChannel"],
            new ConfirmOptions { OkButtonText = L["Confirm"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
        {
            await RunOperationAsync(
                () => Api.DeleteTeamspeakChannelAsync(ServerId, ch.Id),
                L["DeleteChannel"]);
        }
    }

    private async Task EditServerAsync()
    {
        await RunOperationAsync(
            () => Api.EditTeamspeakServerAsync(ServerId, new TeamspeakServerEditRequest
            {
                ServerName = string.IsNullOrWhiteSpace(_editServerName) ? null : _editServerName,
                Password = string.IsNullOrWhiteSpace(_editServerPassword) ? null : _editServerPassword,
                MaxClients = _editServerMaxClients,
                WelcomeMessage = string.IsNullOrWhiteSpace(_editWelcomeMessage) ? null : _editWelcomeMessage
            }),
            L["EditServer"]);
    }

    private async Task SendMessageAsync()
    {
        await RunOperationAsync(
            () => Api.SendTeamspeakGlobalMessageAsync(ServerId, new TeamspeakGlobalMessageRequest
            {
                Message = _globalMessage
            }),
            L["SendMessage"],
            () => _globalMessage = string.Empty);
    }

    private async Task CopyToClipboardAsync(string text)
    {
        await Js.InvokeVoidAsync("navigator.clipboard.writeText", text);
        Toast.Success(L["CopiedToClipboard"]);
    }

    private async Task RefreshBansInlineAsync()
    {
        await _data.RefreshAllAsync();
    }

    private async Task UnbanInlineAsync(int banId)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmUnban"], banId),
            L["Unban"],
            new ConfirmOptions { OkButtonText = L["Confirm"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;
        await RunOperationAsync(
            () => Api.UnbanTeamspeakClientAsync(ServerId, banId),
            L["Unban"]);
    }

    private Task LoadClientsDataAsync(LoadDataArgs args)
    {
        var (page, pageSize, sortBy, descending) = ResolvePage(args, "Nickname");
        return _data.LoadClientsAsync(page, pageSize, search: null, sortBy, descending);
    }

    private Task LoadChannelsDataAsync(LoadDataArgs args)
    {
        var (page, pageSize, sortBy, descending) = ResolvePage(args, "Order");
        return _data.LoadChannelsAsync(page, pageSize, search: null, sortBy, descending);
    }

    private Task LoadBansDataAsync(LoadDataArgs args)
    {
        var (page, pageSize, sortBy, descending) = ResolvePage(args, "Created", true);
        return _data.LoadBansAsync(page, pageSize, search: null, sortBy, descending);
    }

    private static (int Page, int PageSize, string SortBy, bool Descending) ResolvePage(
        LoadDataArgs args, string defaultSort, bool defaultDescending = false)
    {
        var pageSize = args.Top ?? 25;
        var page = ((args.Skip ?? 0) / pageSize) + 1;
        if (string.IsNullOrWhiteSpace(args.OrderBy))
            return (page, pageSize, defaultSort, defaultDescending);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (page, pageSize, parts[0],
            parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildConnectionLink(string hostname, int voicePort) =>
        $"ts3server://{hostname}?port={voicePort}";

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Days > 0
            ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m"
            : $"{ts.Hours}h {ts.Minutes}m";
    }
}
