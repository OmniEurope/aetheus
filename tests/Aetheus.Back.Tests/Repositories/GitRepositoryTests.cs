// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class GitRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GitRepository _repo;
    private readonly int _projectId;

    public GitRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new GitRepository(_db);

        var project = new Project { Name = "P" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetConnectionsByProjectAsync_ReturnsOrderedByRepoName()
    {
        _db.GitConnections.AddRange(
            new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "Zeta", ProviderType = GitProviderType.GitHub },
            new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "Alpha", ProviderType = GitProviderType.GitHub }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetConnectionsByProjectAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].RepositoryName);
    }

    [Fact]
    public async Task GetConnectionDetailAsync_Found_IncludesBranchPolicies()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.BranchPolicies.Add(new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetConnectionDetailAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.BranchPolicies);
    }

    [Fact]
    public async Task GetConnectionDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetConnectionDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindConnectionAsync_Found()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindConnectionAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddConnectionAsync_Persists()
    {
        await _repo.AddConnectionAsync(new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "new", ProviderType = GitProviderType.GitHub }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.GitConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveConnectionAsync_Removes()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "del", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveConnectionAsync(conn, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.GitConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPullRequestsPagedAsync_ReturnsPaged()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PullRequests.AddRange(
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 1, Title = "PR1", SourceBranch = "f1", TargetBranch = "main", AuthorLogin = "user", ExternalCreatedAt = DateTime.UtcNow.AddDays(-1) },
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 2, Title = "PR2", SourceBranch = "f2", TargetBranch = "main", AuthorLogin = "user", ExternalCreatedAt = DateTime.UtcNow },
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 3, Title = "PR3", SourceBranch = "f3", TargetBranch = "main", AuthorLogin = "user", ExternalCreatedAt = DateTime.UtcNow.AddHours(-1) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPullRequestsPagedAsync(conn.Id, null, 1, 2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("PR2", items[0].Title);
    }

    [Fact]
    public async Task GetPullRequestsPagedAsync_WithSearch_Filters()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PullRequests.AddRange(
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 1, Title = "Fix bug", SourceBranch = "f1", TargetBranch = "main", AuthorLogin = "alice", ExternalCreatedAt = DateTime.UtcNow },
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 2, Title = "Add feature", SourceBranch = "f2", TargetBranch = "main", AuthorLogin = "bob", ExternalCreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPullRequestsPagedAsync(conn.Id, "bug", 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPullRequestsPagedAsync_WithStatus_Filters()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PullRequests.AddRange(
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 1, Title = "Open", SourceBranch = "f1", TargetBranch = "main", AuthorLogin = "user", Status = PullRequestStatus.Open, ExternalCreatedAt = DateTime.UtcNow },
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 2, Title = "Merged", SourceBranch = "f2", TargetBranch = "main", AuthorLogin = "user", Status = PullRequestStatus.Merged, ExternalCreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPullRequestsPagedAsync(conn.Id, null, 1, 10, PullRequestStatus.Open, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
        Assert.Equal("Open", items[0].Title);
    }

    [Fact]
    public async Task FindPullRequestByExternalIdAsync_Found()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PullRequests.Add(new PullRequest { GitConnectionId = conn.Id, ExternalId = 42, Title = "PR", SourceBranch = "f", TargetBranch = "main", AuthorLogin = "user", ExternalCreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindPullRequestByExternalIdAsync(conn.Id, 42, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindPullRequestByExternalIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindPullRequestByExternalIdAsync(1, 999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddPullRequestAsync_Persists()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddPullRequestAsync(new PullRequest { GitConnectionId = conn.Id, ExternalId = 1, Title = "New", SourceBranch = "f", TargetBranch = "main", AuthorLogin = "user", ExternalCreatedAt = DateTime.UtcNow }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.PullRequests.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBranchPoliciesAsync_ReturnsOrderedByBranchPattern()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.BranchPolicies.AddRange(
            new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "release/*", PolicyType = BranchPolicyType.RequirePullRequest },
            new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "main", PolicyType = BranchPolicyType.MinimumReviewers }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetBranchPoliciesAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("main", result[0].BranchPattern);
    }

    [Fact]
    public async Task FindBranchPolicyAsync_Found()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var policy = new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest };
        _db.BranchPolicies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindBranchPolicyAsync(policy.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddBranchPolicyAsync_Persists()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddBranchPolicyAsync(new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.BranchPolicies.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveBranchPolicyAsync_Removes()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var policy = new BranchPolicy { GitConnectionId = conn.Id, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest };
        _db.BranchPolicies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveBranchPolicyAsync(policy, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.BranchPolicies.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.GitConnections.Add(new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.GitConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
