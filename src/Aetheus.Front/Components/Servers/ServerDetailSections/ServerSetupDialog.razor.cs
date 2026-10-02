// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

public partial class ServerSetupDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public ServerSetupKind Kind { get; set; }
    [Parameter, EditorRequired] public ServerSetupDialogModel Model { get; set; } = new();

    private ServerSetupDialogModel _model = new();
    // R-511: OmniDialog listens to the keyboard, so every key press re-renders the dialog and hands it the
    // same parameters again. Copying the model there wiped what had just been typed: it is copied once.
    protected override void OnInitialized() => _model = new ServerSetupDialogModel
    {
        Mode = Model.Mode,
        TcpPorts = Model.TcpPorts,
        UdpPorts = Model.UdpPorts,
        MailOnWarning = Model.MailOnWarning
    };
    private void Submit(ServerSetupDialogModel model) => Dialog.Close(model);
}

public enum ServerSetupKind { Portsentry, Rkhunter }

public sealed class ServerSetupDialogModel
{
    [Required, StringLength(32)] public string Mode { get; set; } = "atcp";
    [Required, StringLength(1024)] public string TcpPorts { get; set; } = string.Empty;
    [Required, StringLength(1024)] public string UdpPorts { get; set; } = string.Empty;
    [EmailAddress, StringLength(320)] public string MailOnWarning { get; set; } = string.Empty;
}
