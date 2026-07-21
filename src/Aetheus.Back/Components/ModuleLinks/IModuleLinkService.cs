// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.ModuleLinks;

public interface IModuleLinkService
{
    Task<List<ModuleLinkDto>> GetLinksAsync(int serverId, CancellationToken ct = default);
    Task<List<LinkedResourceDto>> GetLinksForResourceAsync(int serverId, ModuleLinkType sourceType, string sourceIdentifier, CancellationToken ct = default);
    Task<PaginatedResult<LinkedResourceDto>> GetLinksPageAsync(
        int serverId, ModuleLinkPageRequest request, CancellationToken ct = default);
    Task<ModuleLinkDto> CreateLinkAsync(int serverId, CreateModuleLinkRequest request, CancellationToken ct = default);
    Task DeleteLinkAsync(int linkId, CancellationToken ct = default);
    Task AutoDetectLinksAsync(int serverId, CancellationToken ct = default);

    /// <summary>F-04: resolve the owning ServerId for a link (for RBAC on delete).</summary>
    Task<int?> GetServerIdForLinkAsync(int linkId, CancellationToken ct = default);
}
