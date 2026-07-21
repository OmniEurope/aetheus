// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Releases;

public interface IReleaseRepository
{
    Task<(List<Release> Items, int TotalCount)> GetReleasesPagedAsync(
        string? search, int? projectId, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<List<Release>> GetProjectReleasesAsync(int projectId, CancellationToken ct = default);

    Task<Release?> FindReleaseAsync(int id, CancellationToken ct = default);

    Task<Release?> FindByVersionAsync(int projectId, string version, CancellationToken ct = default);

    Task<List<Release>> GetDeployedProjectReleasesAsync(int projectId, CancellationToken ct = default);

    Task AddReleaseAsync(Release release, CancellationToken ct = default);

    Task<Release?> FindByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default);

    Task<Release?> FindPreviousPublishedWithArtifactAsync(Release release, CancellationToken ct = default);

    Task AddRollbackAsync(ReleaseRollback rollback, CancellationToken ct = default);

    Task<ReleaseRollback?> FindRollbackByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default);

    Task<ReleaseRollback?> FindRollbackAsync(int id, CancellationToken ct = default);

    Task<List<Release>> GetByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default);

    Task<int> GetMaxBuildNumberAsync(int projectId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
