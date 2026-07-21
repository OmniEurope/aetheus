// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerCertbotSection
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private bool _actionRunning;
    private string _certSearch = string.Empty;

    // Confirmation dialog
    private bool _confirmVisible;
    private string _confirmTitle = string.Empty;
    private string _confirmMessage = string.Empty;
    private Func<Task>? _confirmAction;

    private CertbotDataDto Certbot => Server.Certbot;

    private List<CertbotCertificateDto> FilteredCertificates => string.IsNullOrWhiteSpace(_certSearch)
        ? Certbot.Certificates
        : Certbot.Certificates.Where(c =>
            c.Name.Contains(_certSearch, StringComparison.OrdinalIgnoreCase) ||
            c.Domains.Any(d => d.Contains(_certSearch, StringComparison.OrdinalIgnoreCase))).ToList();

    private List<string> CertbotResourceNames => Certbot.Certificates.Select(c => c.Name).ToList();

    // Time-dependent, so computed here (browser-local) rather than baked into the DTO contract.
    // ExpiryDate deserializes as Kind=Local in the WASM client, so compare against DateTime.Now.
    private static bool IsExpiringSoon(CertbotCertificateDto cert) => cert.ExpiryDate <= DateTime.Now.AddDays(30);

    private async Task ExecuteActionAsync(CertbotAction action, string? certName = null)
    {
        _actionRunning = true;
        try
        {
            var success = await Api.ExecuteCertbotActionAsync(ServerId, new CertbotActionRequest
            {
                Action = action,
                CertificateName = certName
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

    private async Task OpenCreateDialog()
    {
        await Dialog.OpenAsync<ServerCertbotCreateDialog>(
            L["NewCertificate"],
            new Dictionary<string, object?> { { "ServerId", ServerId } },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true });
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

    private void ConfirmCancelled()
    {
        _confirmVisible = false;
        _confirmAction = null;
    }
}
