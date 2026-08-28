// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Git;

/// <summary>Cache keys for the two bespoke grids on the repository page.
/// <para>S-TECH-SWGT: these two grids keep their own stale-while-revalidate handling rather than going
/// through the generic RevalidateAsync, which would swallow their custom error state.</para></summary>
internal static class GitRepositoryCacheKeys
{
    public static string Commits(int repoId, string? reference, int page, int pageSize, string? search) =>
        $"git-commits:{repoId}:{reference}:{page}:{pageSize}:{search}";

    /// <summary>The sort is part of the key. Without it two different orders share one entry and the
    /// second one is served the first one's rows, which is indistinguishable from a sort that does
    /// nothing.</summary>
    public static string PullRequests(int repoId, int page, int pageSize, string? sortBy, bool sortDescending) =>
        $"git-prs:{repoId}:{page}:{pageSize}:{sortBy}:{sortDescending}";
}
