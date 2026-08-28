// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineRunRepositoryResolver
{
    public static string CommitHref(int? repositoryId, CommitLinkDto commit) => repositoryId is { } id
        ? $"/git-repositories/{id}/commits/{Uri.EscapeDataString(commit.Sha)}"
        : $"/git-repositories/commits/{commit.Id}";

    public static string BranchHref(int? repositoryId, BranchLinkDto branch) => repositoryId is { } id
        ? $"/git-repositories/{id}?tab=branches&branch={Uri.EscapeDataString(branch.Name)}"
        : $"/git-repositories/branches/{branch.Id}";

    public static string BranchHref(int? repositoryId, int? projectId, string branchName) => repositoryId is { } id
        ? $"/git-repositories/{id}?tab=branches&branch={Uri.EscapeDataString(branchName)}"
        : $"/git-repositories?projectId={projectId}";

    public static async Task<int?> ResolveAsync(
        ApiClient api,
        PipelineRunDto run,
        CancellationToken ct = default)
    {
        if (run.ProjectId is not { } projectId) return null;
        try
        {
            var source = await api.Pipelines.GetPipelineSourceAsync(run.PipelineId, ct);
            if (source is { RepositoryId: > 0 }) return source.RepositoryId;
            if (string.IsNullOrWhiteSpace(run.RepositoryUrl)) return null;

            var expectedUrl = GitRepositoryUrl.Canonicalize(run.RepositoryUrl);
            var matches = (await api.Git.GetGitReposAsync(projectId, ct))
                .Where(repo => GitRepositoryUrl.Canonicalize(repo.CloneUrl) == expectedUrl)
                .Select(repo => repo.Id)
                .Distinct()
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        catch (HttpRequestException) { return null; }
    }

}
