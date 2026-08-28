// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Applies an AI-proposed patch to an internal repository and records the mutation.
/// </summary>
public sealed class GitAiPatchService(
    IGitLightRepository lightRepo,
    IGitLightCliService cli,
    IAuditService audit,
    IOptions<GitLightOptions> options)
{
    private readonly GitLightOptions _options = options.Value;

    public async Task<(bool Success, string? CommitSha, string? Error)> ApplyAsync(
        int repoId,
        string branch,
        string patch,
        Func<int, CancellationToken, Task> notifyBranchesChanged,
        CancellationToken ct = default)
    {
        var entity = await lightRepo.FindByIdAsync(repoId, ct).ConfigureAwait(false);
        if (entity is null) return (false, null, "repository not found");

        var diskPath = GitRepoPathResolver.TryResolve(
            _options.RepositoriesPath, entity.ProjectId, entity.Slug)
            ?? throw new BadRequestException("Invalid repository path.");
        var result = await cli.ApplyPatchAsync(
            diskPath,
            branch,
            patch,
            "AI-proposed",
            "Aetheus AI",
            "ai-proposed@aetheus.local",
            ct).ConfigureAwait(false);
        if (!result.Success) return result;

        await audit.LogAsync("AppliedAiPatch", "GitInternalRepo", repoId, branch, ct)
            .ConfigureAwait(false);
        await notifyBranchesChanged(repoId, ct).ConfigureAwait(false);
        return result;
    }
}
