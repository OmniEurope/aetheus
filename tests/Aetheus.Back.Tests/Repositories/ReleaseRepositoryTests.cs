// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Releases;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ReleaseRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ReleaseRepository _repo;
    private readonly int _projectId;

    public ReleaseRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ReleaseRepository(_db);

        var project = new Project { Name = "P" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetReleasesPagedAsync_ReturnsPagedByPublishedAtDesc()
    {
        // Insertion order is deliberately NOT the expected sort order: the newest release is
        // inserted first (lowest Id), so the test fails if ordering falls back to Id instead
        // of PublishedAt.
        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "3.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow },
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Release { ProjectId = _projectId, Version = "2.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetReleasesPagedAsync(null, null, 1, 2, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("3.0.0", items[0].Version);
        Assert.Equal("2.0.0", items[1].Version);
    }

    [Fact]
    public async Task GetReleasesPagedAsync_UnpublishedReleasesSortLast()
    {
        // Regression: DetectedAt was never assigned, so every row tied at default(DateTime) and
        // the newest release never surfaced. Published releases must now lead; unpublished trail.
        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "0.9.0", BranchName = "main", Status = ReleaseStatus.Detected, PublishedAt = null },
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetReleasesPagedAsync(null, null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal("1.0.0", items[0].Version);
        Assert.Equal("0.9.0", items[^1].Version);
    }

    [Fact]
    public async Task GetReleasesPagedAsync_WithSearch_FiltersOnVersionAndBranch()
    {
        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" },
            new Release { ProjectId = _projectId, Version = "2.0.0", BranchName = "release/2.0" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetReleasesPagedAsync("release", null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
        Assert.Equal("2.0.0", items[0].Version);
    }

    [Fact]
    public async Task GetReleasesPagedAsync_WithProjectId_Filters()
    {
        var otherProject = new Project { Name = "Other" };
        _db.Projects.Add(otherProject);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" },
            new Release { ProjectId = otherProject.Id, Version = "2.0.0", BranchName = "main" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetReleasesPagedAsync(null, _projectId, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetReleasesPagedAsync_WithAccessibleIds_Filters()
    {
        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" },
            new Release { ProjectId = _projectId, Version = "2.0.0", BranchName = "main" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _db.Releases.Select(r => r.Id).Take(1).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var (items, total) = await _repo.GetReleasesPagedAsync(null, null, 1, 10, ids, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetReleasesPagedAsync_IncludesLinkedArtifacts()
    {
        var release = new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" };
        release.Artifacts.Add(new PipelineArtifact
        {
            ProjectId = _projectId,
            Name = "validated-images",
            FilePath = "artifact.zip",
            CreatedAt = DateTime.UtcNow,
            RetentionExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        _db.Releases.Add(release);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetReleasesPagedAsync(null, _projectId, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal("validated-images", Assert.Single(Assert.Single(items).Artifacts).Name);
    }

    [Fact]
    public async Task GetProjectReleasesAsync_ReturnsOrderedByPublishedAtDesc()
    {
        // Newest published release inserted first (lowest Id) so an Id-based fallback would fail.
        _db.Releases.AddRange(
            new Release { ProjectId = _projectId, Version = "2.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow },
            new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main", PublishedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetProjectReleasesAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("2.0.0", result[0].Version);
    }

    [Fact]
    public async Task GetProjectReleasesAsync_IncludesLinkedArtifacts()
    {
        var release = new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" };
        release.Artifacts.Add(new PipelineArtifact
        {
            ProjectId = _projectId,
            Name = "validated-images",
            FilePath = "artifact.zip",
            CreatedAt = DateTime.UtcNow,
            RetentionExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        _db.Releases.Add(release);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var items = await _repo.GetProjectReleasesAsync(_projectId, ct: TestContext.Current.CancellationToken);

        Assert.Equal("validated-images", Assert.Single(Assert.Single(items).Artifacts).Name);
    }

    [Fact]
    public async Task FindReleaseAsync_Found_IncludesProject()
    {
        var release = new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" };
        _db.Releases.Add(release);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindReleaseAsync(release.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Project);
    }

    [Fact]
    public async Task FindReleaseAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindReleaseAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByVersionAsync_Found()
    {
        _db.Releases.Add(new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByVersionAsync(_projectId, "1.0.0", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByVersionAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByVersionAsync(_projectId, "unknown", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddReleaseAsync_Persists()
    {
        await _repo.AddReleaseAsync(new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Releases.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindByPipelineRunIdAsync_Found()
    {
        _db.Releases.Add(new Release { ProjectId = _projectId, Version = "1.0.0", BranchName = "main", PipelineRunId = 42 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByPipelineRunIdAsync(42, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByPipelineRunIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByPipelineRunIdAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindPreviousPublishedWithArtifactAsync_UsesOnlyTheSameDeploymentCohort()
    {
        var previousAccept = new Release
        {
            ProjectId = _projectId,
            Version = "1.0.0",
            BranchName = "main",
            Status = ReleaseStatus.Deployed,
            PublishedAt = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        var previousProd = new Release
        {
            ProjectId = _projectId,
            Version = "1.1.0",
            BranchName = "main",
            Status = ReleaseStatus.Deployed,
            PublishedAt = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        var current = new Release
        {
            ProjectId = _projectId,
            Version = "2.0.0",
            BranchName = "main",
            PublishedAt = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc)
        };
        _db.Releases.AddRange(previousAccept, previousProd, current);
        _db.PipelineArtifacts.AddRange(
            new PipelineArtifact { Name = "accept.zip", FilePath = "accept.zip", EnvironmentName = "accept", Releases = [previousAccept] },
            new PipelineArtifact { Name = "prod.zip", FilePath = "prod.zip", EnvironmentName = "prod", Releases = [previousProd] },
            new PipelineArtifact { Name = "current.zip", FilePath = "current.zip", EnvironmentName = "accept", Releases = [current] });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindPreviousPublishedWithArtifactAsync(current, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(previousAccept.Id, result.Id);
        Assert.Single(result.Artifacts);
        Assert.Equal("accept", result.Artifacts[0].EnvironmentName);
    }

    [Fact]
    public async Task FindPreviousPublishedWithArtifactAsync_MissingDeploymentCohort_RefusesRollback()
    {
        var current = new Release
        {
            ProjectId = _projectId,
            Version = "2.0.0",
            BranchName = "main",
            PublishedAt = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc)
        };
        _db.Releases.Add(current);
        _db.PipelineArtifacts.Add(new PipelineArtifact
        {
            Name = "current.zip",
            FilePath = "current.zip",
            Releases = [current]
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindPreviousPublishedWithArtifactAsync(current, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindPreviousPublishedWithArtifactAsync_SkipsAReleaseThatWasRolledBack()
    {
        var previousGood = new Release
        {
            ProjectId = _projectId,
            Version = "1.0.0",
            BranchName = "main",
            Status = ReleaseStatus.Deployed,
            PublishedAt = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        var previousRolledBack = new Release
        {
            ProjectId = _projectId,
            Version = "1.1.0",
            BranchName = "main",
            Status = ReleaseStatus.RolledBack,
            PublishedAt = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        var current = new Release
        {
            ProjectId = _projectId,
            Version = "2.0.0",
            BranchName = "main",
            Status = ReleaseStatus.Deployed,
            PublishedAt = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc)
        };
        _db.Releases.AddRange(previousGood, previousRolledBack, current);
        _db.PipelineArtifacts.AddRange(
            new PipelineArtifact { Name = "good.zip", FilePath = "good.zip", EnvironmentName = "prod", Releases = [previousGood] },
            new PipelineArtifact { Name = "rolled-back.zip", FilePath = "rolled-back.zip", EnvironmentName = "prod", Releases = [previousRolledBack] },
            new PipelineArtifact { Name = "current.zip", FilePath = "current.zip", EnvironmentName = "prod", Releases = [current] });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindPreviousPublishedWithArtifactAsync(current, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(previousGood.Id, result.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.Releases.Add(new Release { ProjectId = _projectId, Version = "v", BranchName = "b" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Releases.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
