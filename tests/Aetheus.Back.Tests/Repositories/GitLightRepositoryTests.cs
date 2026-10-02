// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class GitLightRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GitLightRepository _repo;
    private readonly int _projectId;

    public GitLightRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new GitLightRepository(_db);

        var project = new Project { Name = "P" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetByProjectAsync_ReturnsOrderedByName()
    {
        _db.GitInternalRepos.AddRange(
            new GitInternalRepo { ProjectId = _projectId, Name = "Zeta", Slug = "zeta" },
            new GitInternalRepo { ProjectId = _projectId, Name = "Alpha", Slug = "alpha" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByProjectAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetByProjectAsync_FiltersOtherProjects()
    {
        var other = new Project { Name = "Other" };
        _db.Projects.Add(other);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.GitInternalRepos.AddRange(
            new GitInternalRepo { ProjectId = _projectId, Name = "R1", Slug = "r1" },
            new GitInternalRepo { ProjectId = other.Id, Name = "R2", Slug = "r2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByProjectAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task FindByIdAsync_Found()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByIdAsync(repo.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByIdAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByIdWithProjectAsync_Found_IncludesProject()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByIdWithProjectAsync(repo.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Project);
    }

    [Fact]
    public async Task FindByIdWithProjectAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByIdWithProjectAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindBySlugAsync_Found_IncludesProject()
    {
        _db.GitInternalRepos.Add(new GitInternalRepo { ProjectId = _projectId, Name = "MyRepo", Slug = "my-repo" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindBySlugAsync(_projectId, "my-repo", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Project);
    }

    [Fact]
    public async Task FindBySlugAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindBySlugAsync(_projectId, "nonexistent", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRepos()
    {
        _db.GitInternalRepos.AddRange(
            new GitInternalRepo { ProjectId = _projectId, Name = "R1", Slug = "r1" },
            new GitInternalRepo { ProjectId = _projectId, Name = "R2", Slug = "r2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAccessibleAsync_NullProjectIds_ReturnsAllReposOrderedByProjectThenName()
    {
        // null = unrestricted (admin) -> every repo, ordered by project name then repo name.
        var beta = new Project { Name = "Beta" };
        _db.Projects.Add(beta); // "P" (this._projectId) sorts after "Beta"
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.GitInternalRepos.AddRange(
            new GitInternalRepo { ProjectId = _projectId, Name = "Zeta", Slug = "zeta" },
            new GitInternalRepo { ProjectId = beta.Id, Name = "Alpha", Slug = "alpha" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAccessibleAsync(null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name); // project "Beta" < project "P"
        Assert.Equal("Zeta", result[1].Name);
        Assert.NotNull(result[0].Project);
    }

    [Fact]
    public async Task GetAccessibleAsync_RestrictedIds_ExcludesReposOfOtherProjects()
    {
        // The security guarantee: a caller scoped to [_projectId] must NOT receive another project's repos.
        var other = new Project { Name = "Other" };
        _db.Projects.Add(other);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.GitInternalRepos.AddRange(
            new GitInternalRepo { ProjectId = _projectId, Name = "Mine", Slug = "mine" },
            new GitInternalRepo { ProjectId = other.Id, Name = "NotMine", Slug = "not-mine" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAccessibleAsync([_projectId], ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Mine", result[0].Name);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new GitInternalRepo { ProjectId = _projectId, Name = "New", Slug = "new" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.GitInternalRepos.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "Del", Slug = "del" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(repo, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.GitInternalRepos.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetNextPrNumberAsync_ReturnsNextAfterMax()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PullRequests.AddRange(
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 3, Title = "PR3", SourceBranch = "f", TargetBranch = "main", AuthorLogin = "u", ExternalCreatedAt = DateTime.UtcNow },
            new PullRequest { GitConnectionId = conn.Id, ExternalId = 5, Title = "PR5", SourceBranch = "f", TargetBranch = "main", AuthorLogin = "u", ExternalCreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetNextPrNumberAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(6, result);
    }

    [Fact]
    public async Task GetNextPrNumberAsync_NoPrs_Returns1()
    {
        var conn = new GitConnection { ProjectId = _projectId, OwnerOrGroup = "org", RepositoryName = "repo", ProviderType = GitProviderType.GitHub };
        _db.GitConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetNextPrNumberAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetBranchProtectionRulesAsync_ReturnsOrderedByPattern()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.BranchProtectionRules.AddRange(
            new BranchProtectionRule { GitInternalRepoId = repo.Id, Pattern = "release/*" },
            new BranchProtectionRule { GitInternalRepoId = repo.Id, Pattern = "main" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetBranchProtectionRulesAsync(repo.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("main", result[0].Pattern);
    }

    [Fact]
    public async Task FindBranchProtectionRuleAsync_Found()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rule = new BranchProtectionRule { GitInternalRepoId = repo.Id, Pattern = "main" };
        _db.BranchProtectionRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindBranchProtectionRuleAsync(rule.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddBranchProtectionRuleAsync_Persists()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddBranchProtectionRuleAsync(new BranchProtectionRule { GitInternalRepoId = repo.Id, Pattern = "main" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.BranchProtectionRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveBranchProtectionRuleAsync_Removes()
    {
        var repo = new GitInternalRepo { ProjectId = _projectId, Name = "R", Slug = "r" };
        _db.GitInternalRepos.Add(repo);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rule = new BranchProtectionRule { GitInternalRepoId = repo.Id, Pattern = "main" };
        _db.BranchProtectionRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveBranchProtectionRuleAsync(rule, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.BranchProtectionRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.GitInternalRepos.Add(new GitInternalRepo { ProjectId = _projectId, Name = "P", Slug = "p" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.GitInternalRepos.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
