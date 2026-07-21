// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class GitGraphRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GitGraphRepository _repo;

    public GitGraphRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new GitGraphRepository(_db);

        _db.Projects.Add(new Project { Id = 1, Name = "Demo" });
        _db.GitCommits.AddRange(
            new GitCommit { Id = 1, ProjectId = 1, Sha = "aaa111", CommittedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new GitCommit { Id = 2, ProjectId = 1, Sha = "bbb222", CommittedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc) });
        _db.GitBranches.AddRange(
            new GitBranch { Id = 1, ProjectId = 1, Name = "main" },
            new GitBranch { Id = 2, ProjectId = 1, Name = "develop" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FindCommitAsync_FoundAndMissing()
    {
        Assert.NotNull(await _repo.FindCommitAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindCommitAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindBranchAsync_FoundAndMissing()
    {
        Assert.NotNull(await _repo.FindBranchAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindBranchAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindProjectCommitsAsync_OrdersNewestFirstAndLimits()
    {
        var commits = await _repo.FindProjectCommitsAsync(1, 1, ct: TestContext.Current.CancellationToken);

        Assert.Single(commits);
        Assert.Equal("bbb222", commits[0].Sha); // newest by CommittedAt
    }

    [Fact]
    public async Task FindProjectBranchesAsync_OrdersByName()
    {
        var branches = await _repo.FindProjectBranchesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, branches.Count);
        Assert.Equal("develop", branches[0].Name);
    }

    [Fact]
    public async Task FindCommitByShaAsync_FoundAndMissing()
    {
        Assert.NotNull(await _repo.FindCommitByShaAsync(1, "aaa111", ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindCommitByShaAsync(1, "nope", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindBranchByNameAsync_FoundAndMissing()
    {
        Assert.NotNull(await _repo.FindBranchByNameAsync(1, "main", ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindBranchByNameAsync(1, "ghost", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindCommitByShaReadOnlyAsync_Found()
        => Assert.NotNull(await _repo.FindCommitByShaReadOnlyAsync(1, "bbb222", ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task FindBranchByNameReadOnlyAsync_Found()
        => Assert.NotNull(await _repo.FindBranchByNameReadOnlyAsync(1, "develop", ct: TestContext.Current.CancellationToken));
}
