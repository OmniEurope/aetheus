// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ModuleLinks;

public interface IModuleLinkRepository
{
    Task<List<ModuleLink>> GetLinksAsync(int serverId, CancellationToken ct = default);
    Task<List<ModuleLink>> GetLinksForResourceAsync(int serverId, ModuleLinkType sourceType, string sourceIdentifier, CancellationToken ct = default);
    Task<(List<ModuleLink> Items, int TotalCount)> GetLinksPageAsync(
        int serverId, ModuleLinkPageRequest request, CancellationToken ct = default);
    Task<ModuleLink?> FindLinkAsync(int id, CancellationToken ct = default);
    Task AddAsync(ModuleLink link, CancellationToken ct = default);
    Task RemoveAsync(ModuleLink link, CancellationToken ct = default);
    Task RemoveAutoDetectedAsync(int serverId, CancellationToken ct = default);
    Task<bool> LinkExistsAsync(int serverId, ModuleLinkType sourceType, string sourceIdentifier, ModuleLinkType targetType, string targetIdentifier, CancellationToken ct = default);
    Task<List<ApacheVirtualHost>> GetApacheVhostsAsync(int serverId, CancellationToken ct = default);
    Task<List<CertbotCertificate>> GetCertbotCertsAsync(int serverId, CancellationToken ct = default);
    Task AddLinksAsync(List<ModuleLink> links, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
