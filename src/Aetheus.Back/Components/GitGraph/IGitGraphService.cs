// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.GitGraph;

public interface IGitGraphService
{
    Task<GitCommitDto?> GetCommitAsync(int id, CancellationToken ct = default);
    Task<GitBranchDto?> GetBranchAsync(int id, CancellationToken ct = default);
    Task<ProjectGitGraphDto> GetProjectGraphAsync(int projectId, CancellationToken ct = default);
}
