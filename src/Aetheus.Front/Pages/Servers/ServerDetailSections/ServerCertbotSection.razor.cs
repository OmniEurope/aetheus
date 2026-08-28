// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerCertbotSection : ServerActionSectionBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private string _certSearch = string.Empty;

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
        await ExecuteServerActionAsync(() => Api.ServerTools.ExecuteCertbotActionAsync(
            ServerId, new CertbotActionRequest
            {
                Action = action,
                CertificateName = certName
            }));
    }

    private async Task OpenCreateDialog()
    {
        await Dialog.OpenAsync<ServerCertbotCreateDialog>(
            L["NewCertificate"],
            new Dictionary<string, object?> { { "ServerId", ServerId } },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
    }

}
