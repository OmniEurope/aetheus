// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Portsentry;

public interface IPortsentryService
{
    Task<PortsentryDataDto> GetStateAsync(int serverId, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, PortsentryActionRequest request, CancellationToken ct = default);
    Task SetupAsync(int serverId, PortsentrySetupRequest request, CancellationToken ct = default);
    Task GetLogsAsync(int serverId, PortsentryLogRequest request, CancellationToken ct = default);
    Task UnblockIpAsync(int serverId, PortsentryUnblockRequest request, CancellationToken ct = default);
    Task GetStatusAsync(int serverId, CancellationToken ct = default);
    Task<List<PortsentryWhitelistIpDto>> GetWhitelistAsync(int serverId, CancellationToken ct = default);
    Task<PaginatedResult<PortsentryBlockedIpDto>> GetBlockedIpsAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<PortsentryWhitelistIpDto>> GetWhitelistAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task<PortsentryFilterValuesDto> GetFilterValuesAsync(int serverId, CancellationToken ct = default);
    Task<PortsentryWhitelistIpDto> AddWhitelistIpAsync(int serverId, AddPortsentryWhitelistRequest request, CancellationToken ct = default);
    Task<bool> RemoveWhitelistIpAsync(int serverId, int id, CancellationToken ct = default);
}
