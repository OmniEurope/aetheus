// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Git;

internal static class GitPullRequestResolver
{
    public static async Task<(GitInternalRepo? Repository, PullRequest? PullRequest)> FindOpenAsync(
        IGitLightRepository lightRepository,
        IGitRepository gitRepository,
        int repositoryId,
        int pullRequestId,
        CancellationToken ct)
    {
        var repository = await lightRepository.FindByIdAsync(repositoryId, ct).ConfigureAwait(false);
        if (repository?.GitConnectionId is not { } connectionId) return (null, null);
        var pullRequest = await gitRepository
            .FindPullRequestByExternalIdAsync(connectionId, pullRequestId, ct)
            .ConfigureAwait(false);
        return pullRequest?.Status == PullRequestStatus.Open
            ? (repository, pullRequest)
            : (null, null);
    }
}
