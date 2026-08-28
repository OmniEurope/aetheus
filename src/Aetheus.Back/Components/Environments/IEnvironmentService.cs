// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Environments;

public interface IEnvironmentService
{
    Task<PaginatedResult<EnvironmentDto>> GetEnvironmentsAsync(int? projectId, PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default);
    Task<EnvironmentDto?> GetEnvironmentAsync(int id, CancellationToken ct = default);
    Task<EnvironmentDto> CreateEnvironmentAsync(CreateEnvironmentRequest request, CancellationToken ct = default);
    Task<EnvironmentDto?> UpdateEnvironmentAsync(int id, UpdateEnvironmentRequest request, CancellationToken ct = default);
    Task<bool> DeleteEnvironmentAsync(int id, CancellationToken ct = default);
    Task<EnvironmentDto?> DuplicateEnvironmentAsync(int envId, int? targetProjectId, CancellationToken ct = default);
    Task<int?> GetProjectServerProjectIdAsync(int projectServerId, CancellationToken ct = default);
    Task<bool> LinkProjectServerAsync(int envId, int projectServerId, CancellationToken ct = default);
    Task<bool> UnlinkProjectServerAsync(int envId, int projectServerId, CancellationToken ct = default);
}
