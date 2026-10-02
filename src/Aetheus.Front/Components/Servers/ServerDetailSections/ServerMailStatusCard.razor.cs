// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>PLAN-005: what the heartbeat inventory says about the mail stack (ownership, helper version,
/// TLS, OpenDKIM, spam filter, collector findings) and the certificate actions.</summary>
public partial class ServerMailStatusCard
{
    internal const int CurrentHelperVersion = 2;

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public MailDataDto Mail { get; set; } = new();
    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter] public bool CanManage { get; set; }

    private bool _busy;

    private bool HostnameKnown => MailValidation.IsValidDomainName(Mail.Hostname);

    internal bool UsesLetsEncryptLineage =>
        HostnameKnown && Mail.Tls.CertPath == $"/etc/letsencrypt/live/{Mail.Hostname}/fullchain.pem";

    internal string TlsText
    {
        get
        {
            if (string.IsNullOrEmpty(Mail.Tls.CertPath)) return L["MailTlsNone"];
            if (!Mail.Tls.IsReadable) return $"{L["MailTlsUnreadable"]} ({Mail.Tls.CertPath})";
            var validity = Mail.Tls.ExpiresAt is { } expires
                ? string.Format(CultureInfo.CurrentCulture, L["MailTlsValidUntil"], expires.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
                : string.Empty;
            return string.IsNullOrEmpty(Mail.Tls.Issuer) ? validity : $"{validity} ({Mail.Tls.Issuer})";
        }
    }

    private OmniTone SpamBadgeStyle => (Mail.SpamFilter.IsInstalled, Mail.SpamFilter.IsRunning) switch
    {
        (true, true) => OmniTone.Success,
        (true, false) => OmniTone.Danger,
        _ => OmniTone.Neutral
    };

    private string SpamText => Mail.SpamFilter.IsInstalled
        ? $"{L["MailSpamFilter"]} {Mail.SpamFilter.Name} {Mail.SpamFilter.Version} {(Mail.SpamFilter.IsRunning ? L["Running"] : L["Stopped"])}".Trim()
        : L["MailSpamNotInstalled"];

    private async Task RestartOpenDkimAsync()
    {
        _busy = true;
        try
        {
            var queued = await Api.Mail.QueueMailActionAsync(ServerId, new MailActionRequest { Action = MailAction.RestartOpenDkim });
            if (queued is not null) Toast.Success(L["TaskQueued"]);
            else Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RequestCertificateAsync()
    {
        var result = await Dialog.OpenAsync<MailOperationDialog>(L["MailObtainCertificate"],
            new Dictionary<string, object?>
            {
                { "Mode", MailDialogMode.RequestCertificate },
                { "Model", new MailDialogModel { Hostname = Mail.Hostname } }
            },
            new OmniDialogOptions { Width = "34rem", AutoFocusFirstElement = false });
        if (result is not MailDialogModel model) return;

        _busy = true;
        try
        {
            var queued = await Api.Mail.RequestMailCertificateAsync(ServerId, new RequestMailCertificateRequest { Email = model.Email });
            if (queued is not null) Toast.Success(L["TaskQueued"]);
            else Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task InstallCertificateAsync()
    {
        _busy = true;
        try
        {
            var queued = await Api.Mail.InstallMailCertificateAsync(ServerId);
            if (queued is not null) Toast.Success(L["TaskQueued"]);
            else Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _busy = false;
        }
    }
}
