// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Rkhunter;

public interface IRkhunterService
{
    Task<RkhunterDataDto> GetStateAsync(int serverId, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, RkhunterActionRequest request, CancellationToken ct = default);
    Task SetupAsync(int serverId, RkhunterSetupRequest request, CancellationToken ct = default);
    Task GetLogsAsync(int serverId, RkhunterLogRequest request, CancellationToken ct = default);
    Task<List<RkhunterWarningDto>> GetWarningsAsync(int serverId, bool includeArchived = false, CancellationToken ct = default);
    Task<List<RkhunterScanResultDto>> GetScanHistoryAsync(int serverId, int limit = 50, CancellationToken ct = default);
    Task SetScheduleAsync(int serverId, RkhunterScheduleRequest request, CancellationToken ct = default);
}
