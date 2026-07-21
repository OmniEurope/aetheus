// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Apache;

public interface IApacheService
{
    Task<ApacheDataDto> GetStateAsync(int serverId, CancellationToken ct = default);
    Task<List<ApacheModuleDto>> GetModulesAsync(int serverId, CancellationToken ct = default);
    Task<List<ApacheVirtualHostDto>> GetVirtualHostsAsync(int serverId, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, ApacheActionRequest request, CancellationToken ct = default);
    Task GetLogsAsync(int serverId, ApacheLogRequest request, CancellationToken ct = default);
    Task GetVHostConfigAsync(int serverId, string siteName, CancellationToken ct = default);
    Task SaveVHostConfigAsync(int serverId, ApacheVHostSaveRequest request, CancellationToken ct = default);
    Task GetHtaccessAsync(int serverId, string documentRoot, CancellationToken ct = default);
    Task SaveHtaccessAsync(int serverId, ApacheHtaccessSaveRequest request, CancellationToken ct = default);
}
