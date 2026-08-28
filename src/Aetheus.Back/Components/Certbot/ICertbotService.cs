// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Certbot;

public interface ICertbotService
{
    Task<List<CertbotCertificateDto>> GetCertificatesAsync(int serverId, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, CertbotActionRequest request, CancellationToken ct = default);
    Task CreateCertificateAsync(int serverId, CertbotCreateRequest request, CancellationToken ct = default);
}
