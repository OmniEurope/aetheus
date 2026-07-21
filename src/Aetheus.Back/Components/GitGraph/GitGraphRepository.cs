// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.GitGraph;

public sealed class GitGraphRepository(AppDbContext db) : IGitGraphRepository
{
    public async Task<GitCommit?> FindCommitAsync(int id, CancellationToken ct = default) =>
        await db.GitCommits
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Include(c => c.Project)
            .Include(c => c.Releases)
            .Include(c => c.Artifacts)
            .Include(c => c.Branches)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<GitBranch?> FindBranchAsync(int id, CancellationToken ct = default) =>
        await db.GitBranches
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Include(b => b.Project)
            .Include(b => b.Releases)
            .Include(b => b.Artifacts)
            .Include(b => b.Commits)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<List<GitCommit>> FindProjectCommitsAsync(int projectId, int limit, CancellationToken ct = default) =>
        await db.GitCommits
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .Include(c => c.Project)
            .Include(c => c.Releases)
            .Include(c => c.Artifacts)
            .Include(c => c.Branches)
            .OrderByDescending(c => c.CommittedAt ?? c.CreatedAt)
            .Take(limit)
            .AsSplitQuery()
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<List<GitBranch>> FindProjectBranchesAsync(int projectId, CancellationToken ct = default) =>
        await db.GitBranches
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<GitCommit?> FindCommitByShaAsync(int projectId, string sha, CancellationToken ct = default) =>
        await db.GitCommits
            .Where(c => c.ProjectId == projectId && c.Sha == sha)
            .Include(c => c.Branches)
            .Include(c => c.Releases)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<GitBranch?> FindBranchByNameAsync(int projectId, string name, CancellationToken ct = default) =>
        await db.GitBranches
            .Where(b => b.ProjectId == projectId && b.Name == name)
            .Include(b => b.Releases)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public void TrackCommit(GitCommit commit) => db.GitCommits.Add(commit);

    public void TrackBranch(GitBranch branch) => db.GitBranches.Add(branch);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

    public async Task<GitCommit?> FindCommitByShaReadOnlyAsync(int projectId, string sha, CancellationToken ct = default) =>
        await db.GitCommits
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.Sha == sha)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<GitBranch?> FindBranchByNameReadOnlyAsync(int projectId, string name, CancellationToken ct = default) =>
        await db.GitBranches
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.Name == name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
}
