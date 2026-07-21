// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.ServerApps;

public interface IServerAppService
{
    Task<List<ServerAppDto>> GetByServerIdAsync(int serverId, CancellationToken ct = default);
    Task<PaginatedResult<ServerAppDto>> GetPageAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task<ServerAppDto?> GetByIdAsync(int serverId, int id, CancellationToken ct = default);
    Task<ServerAppDto> CreateAsync(int serverId, CreateServerAppRequest request, CancellationToken ct = default);
    Task<ServerAppDto?> UpdateAsync(int serverId, int id, UpdateServerAppRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int serverId, int id, CancellationToken ct = default);
}
