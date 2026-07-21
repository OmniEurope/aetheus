// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public interface IGitCliService
{
    Task<List<(string BranchName, string Version)>> ListReleaseBranchesAsync(string repositoryUrl, CancellationToken ct = default);
}
