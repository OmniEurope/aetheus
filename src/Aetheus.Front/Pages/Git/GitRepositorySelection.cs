// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Git;

internal static class GitRepositorySelection
{
    public static int? Resolve(IReadOnlyCollection<GitLightRepoDto> repositories, string? repositoryUrl)
    {
        if (repositories.Count == 0) return null;
        if (repositories.Count == 1) return repositories.First().Id;

        var expected = GitRepositoryUrl.Canonicalize(repositoryUrl);
        if (expected is null) return null;

        var matches = repositories
            .Where(repository => GitRepositoryUrl.Canonicalize(repository.CloneUrl) == expected)
            .Select(repository => repository.Id)
            .Distinct()
            .Take(2)
            .ToList();
        if (matches.Count == 1) return matches[0];

        // Local launchers can expose the same smart-HTTP repository through another host/port.
        // The stable /git/{project}/{slug} path still identifies it in that situation.
        var expectedPath = GitRepositoryUrl.CanonicalPath(repositoryUrl);
        if (expectedPath is null) return null;

        matches = repositories
            .Where(repository => GitRepositoryUrl.CanonicalPath(repository.CloneUrl) == expectedPath)
            .Select(repository => repository.Id)
            .Distinct()
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

}
