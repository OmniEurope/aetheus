// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.GitGraph;

public sealed class GitGraphService(IGitGraphRepository repo) : IGitGraphService
{
    public async Task<GitCommitDto?> GetCommitAsync(int id, CancellationToken ct = default)
    {
        var c = await repo.FindCommitAsync(id, ct).ConfigureAwait(false);
        if (c is null) return null;

        return new GitCommitDto
        {
            Id = c.Id,
            ProjectId = c.ProjectId,
            ProjectName = c.Project?.Name,
            Sha = c.Sha,
            Message = c.Message,
            Author = c.Author,
            CommittedAt = c.CommittedAt,
            RepositoryUrl = c.Project?.RepositoryUrl,
            Releases = c.Releases.Select(GitGraphMapper.ToLink).ToList(),
            Artifacts = c.Artifacts.Select(GitGraphMapper.ToLink).ToList(),
            Branches = c.Branches.Select(GitGraphMapper.ToLink).ToList()
        };
    }

    // S-FEAT-29: most-recent commits for a project with their cross-links, for the project git-graph view.
    private const int ProjectGraphCommitLimit = 200;

    public async Task<ProjectGitGraphDto> GetProjectGraphAsync(int projectId, CancellationToken ct = default)
    {
        var commits = await repo.FindProjectCommitsAsync(projectId, ProjectGraphCommitLimit, ct).ConfigureAwait(false);
        var branches = await repo.FindProjectBranchesAsync(projectId, ct).ConfigureAwait(false);

        var first = commits.FirstOrDefault();
        return new ProjectGitGraphDto
        {
            ProjectId = projectId,
            ProjectName = first?.Project?.Name,
            RepositoryUrl = first?.Project?.RepositoryUrl,
            Commits = commits.Select(c => new GitCommitDto
            {
                Id = c.Id,
                ProjectId = c.ProjectId,
                ProjectName = c.Project?.Name,
                Sha = c.Sha,
                Message = c.Message,
                Author = c.Author,
                CommittedAt = c.CommittedAt,
                RepositoryUrl = c.Project?.RepositoryUrl,
                Releases = c.Releases.Select(GitGraphMapper.ToLink).ToList(),
                Artifacts = c.Artifacts.Select(GitGraphMapper.ToLink).ToList(),
                Branches = c.Branches.Select(GitGraphMapper.ToLink).ToList()
            }).ToList(),
            Branches = branches.Select(GitGraphMapper.ToLink).ToList()
        };
    }

    public async Task<GitBranchDto?> GetBranchAsync(int id, CancellationToken ct = default)
    {
        var b = await repo.FindBranchAsync(id, ct).ConfigureAwait(false);
        if (b is null) return null;

        return new GitBranchDto
        {
            Id = b.Id,
            ProjectId = b.ProjectId,
            ProjectName = b.Project?.Name,
            Name = b.Name,
            RepositoryUrl = b.Project?.RepositoryUrl,
            Releases = b.Releases.Select(GitGraphMapper.ToLink).ToList(),
            Artifacts = b.Artifacts.Select(GitGraphMapper.ToLink).ToList(),
            Commits = b.Commits.Select(GitGraphMapper.ToLink).ToList()
        };
    }
}
