// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Certbot;

public interface ICertbotRepository
{
    Task<List<CertbotCertificate>> GetCertificatesAsync(int serverId, CancellationToken ct = default);
    Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
}
