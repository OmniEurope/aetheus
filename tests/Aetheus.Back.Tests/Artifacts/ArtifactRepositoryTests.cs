// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Artifacts;

public class ArtifactRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ArtifactRepository _repo;

    public ArtifactRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ArtifactRepository(_db);

        // FindAsync / GetByProjectPagedAsync Include the REQUIRED Pipeline + PipelineRun navigations;
        // EF filters out rows whose required principal is missing, so seed them up front.
        _db.Pipelines.AddRange(
            new Pipeline { Id = 1, Name = "CI" },
            new Pipeline { Id = 2, Name = "CD" });
        _db.PipelineRuns.Add(new PipelineRun { Id = 1, PipelineId = 1, Status = PipelineStatus.Success });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PipelineArtifact MakeArtifact(
        int pipelineId = 1, int projectId = 1, int runId = 1,
        ArtifactRetentionPolicy policy = ArtifactRetentionPolicy.Build,
        string? env = null, DateTime? createdAt = null, DateTime? expiresAt = null,
        string name = "build.zip")
        => new()
        {
            PipelineId = pipelineId,
            ProjectId = projectId,
            PipelineRunId = runId,
            Name = name,
            FilePath = $"/artifacts/{name}",
            RetentionPolicy = policy,
            EnvironmentName = env,
            CreatedAt = createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RetentionExpiresAt = expiresAt ?? new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

    [Fact]
    public async Task FindAsync_Found_ReturnsArtifact()
    {
        var a = MakeArtifact();
        await _repo.AddAsync(a, ct: TestContext.Current.CancellationToken);

        var found = await _repo.FindAsync(a.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("build.zip", found.Name);
    }

    [Fact]
    public async Task FindAsync_NotFound_ReturnsNull()
        => Assert.Null(await _repo.FindAsync(999, ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetByProjectPagedAsync_FiltersByProjectAndPaginates()
    {
        await _repo.AddAsync(MakeArtifact(projectId: 1, createdAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), name: "a3"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), name: "a1"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 2, name: "other"), ct: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetByProjectPagedAsync(1, null, null, 1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        // Project filter keeps only project 1's artifacts (the InMemory split-query provider does not
        // guarantee positional order, so assert set membership rather than index).
        Assert.Contains(items, a => a.Name == "a3");
        Assert.Contains(items, a => a.Name == "a1");
        Assert.DoesNotContain(items, a => a.Name == "other");
    }

    [Fact]
    public async Task GetByProjectPagedAsync_FiltersByPolicyAndPipeline()
    {
        await _repo.AddAsync(MakeArtifact(projectId: 1, pipelineId: 1, policy: ArtifactRetentionPolicy.Released), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, pipelineId: 1, policy: ArtifactRetentionPolicy.Build), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, pipelineId: 2, policy: ArtifactRetentionPolicy.Released), ct: TestContext.Current.CancellationToken);

        var (released, releasedTotal) = await _repo.GetByProjectPagedAsync(1, ArtifactRetentionPolicy.Released, null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, releasedTotal);

        var (pipe1, pipe1Total) = await _repo.GetByProjectPagedAsync(1, ArtifactRetentionPolicy.Released, 1, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, pipe1Total);
        Assert.Single(pipe1);
    }

    [Fact]
    public async Task GetByPipelineAndProjectAsync_MatchesAllThree()
    {
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Deployed), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Build), ct: TestContext.Current.CancellationToken);

        var result = await _repo.GetByPipelineAndProjectAsync(1, 1, ArtifactRetentionPolicy.Deployed, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(ArtifactRetentionPolicy.Deployed, result[0].RetentionPolicy);
    }

    [Fact]
    public async Task GetByEnvironmentAsync_ReturnsDeployedForEnvironment()
    {
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Deployed, env: "prod"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Deployed, env: "staging"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Build, env: "prod"), ct: TestContext.Current.CancellationToken);

        var result = await _repo.GetByEnvironmentAsync(1, 1, "prod", ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("prod", result[0].EnvironmentName);
    }

    [Fact]
    public async Task GetReleasesAsync_ReturnsOnlyReleasedPolicy()
    {
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Released), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 1, projectId: 1, policy: ArtifactRetentionPolicy.Build), ct: TestContext.Current.CancellationToken);

        var result = await _repo.GetReleasesAsync(1, 1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(ArtifactRetentionPolicy.Released, result[0].RetentionPolicy);
    }

    [Fact]
    public async Task GetExpiredAsync_ReturnsArtifactsAtOrBeforeCutoffOrderedByExpiry()
    {
        await _repo.AddAsync(MakeArtifact(expiresAt: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), name: "exp-early"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(expiresAt: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), name: "exp-late"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(expiresAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), name: "not-expired"), ct: TestContext.Current.CancellationToken);

        var cutoff = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var result = await _repo.GetExpiredAsync(cutoff, 10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("exp-early", result[0].Name);
        Assert.Equal("exp-late", result[1].Name);
    }

    [Fact]
    public async Task GetExpiredAsync_RespectsBatchSize()
    {
        for (var i = 0; i < 5; i++)
            await _repo.AddAsync(MakeArtifact(expiresAt: new DateTime(2026, 1, i + 1, 0, 0, 0, DateTimeKind.Utc), name: $"e{i}"), ct: TestContext.Current.CancellationToken);

        var result = await _repo.GetExpiredAsync(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 3, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetProjectBuildArtifactsAsync_ReturnsOnlyProjectBuildsOldestFirst()
    {
        var linkedBuild = MakeArtifact(projectId: 1, policy: ArtifactRetentionPolicy.Build,
            createdAt: new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), name: "linked-release");
        await _repo.AddAsync(MakeArtifact(projectId: 1, policy: ArtifactRetentionPolicy.Build,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), name: "old"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, policy: ArtifactRetentionPolicy.Build,
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), name: "new"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, policy: ArtifactRetentionPolicy.Released, name: "released"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 2, policy: ArtifactRetentionPolicy.Build, name: "other-project"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(linkedBuild, ct: TestContext.Current.CancellationToken);
        var release = new Release { Id = 51, ProjectId = 1, Version = "1.0.0" };
        release.Artifacts.Add(linkedBuild);
        _db.Releases.Add(release);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetProjectBuildArtifactsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(["old", "new"], result.Select(a => a.Name));
    }

    [Fact]
    public async Task LinkReleaseAsync_ExistingRelease_LinksOnce()
    {
        var a = MakeArtifact();
        await _repo.AddAsync(a, ct: TestContext.Current.CancellationToken);
        _db.Releases.Add(new Release { Id = 50, ProjectId = 1, Version = "1.0.0" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.LinkReleaseAsync(a, 50, ct: TestContext.Current.CancellationToken);
        await _repo.LinkReleaseAsync(a, 50, ct: TestContext.Current.CancellationToken); // idempotent
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(a.Releases);
        Assert.Equal(50, a.Releases.First().Id);
    }

    [Fact]
    public async Task LinkReleaseAsync_MissingRelease_NoOp()
    {
        var a = MakeArtifact();
        await _repo.AddAsync(a, ct: TestContext.Current.CancellationToken);

        await _repo.LinkReleaseAsync(a, 999, ct: TestContext.Current.CancellationToken);

        Assert.Empty(a.Releases);
    }

    [Fact]
    public async Task RemoveAsync_DeletesArtifact()
    {
        var a = MakeArtifact();
        await _repo.AddAsync(a, ct: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(a, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.PipelineArtifacts.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetNextBuildNumberAsync_NoArtifacts_ReturnsOne()
        => Assert.Equal(1, await _repo.GetNextBuildNumberAsync(1, 1, ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetNextBuildNumberAsync_ReturnsMaxRunIdPlusOne()
    {
        await _repo.AddAsync(MakeArtifact(projectId: 1, pipelineId: 1, runId: 7), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(projectId: 1, pipelineId: 1, runId: 3), ct: TestContext.Current.CancellationToken);

        Assert.Equal(8, await _repo.GetNextBuildNumberAsync(1, 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindSuccessfulPipelineArtifactByCommitAsync_RequiresExactSuccessfulProvenance()
    {
        const string expectedCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        _db.Projects.Add(new Project { Id = 7, Name = "Aetheus" });
        _db.Pipelines.Add(new Pipeline { Id = 3, Name = "aetheus-ci", ProjectId = 7 });
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 3, PipelineId = 3, CommitHash = expectedCommit, Status = PipelineStatus.Success },
            new PipelineRun { Id = 4, PipelineId = 3, CommitHash = expectedCommit, Status = PipelineStatus.Failed },
            new PipelineRun { Id = 5, PipelineId = 3, CommitHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", Status = PipelineStatus.Success });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 3, projectId: 7, runId: 3, name: "package"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 3, projectId: 7, runId: 4, name: "failed"), ct: TestContext.Current.CancellationToken);
        await _repo.AddAsync(MakeArtifact(pipelineId: 3, projectId: 7, runId: 5, name: "other-commit"), ct: TestContext.Current.CancellationToken);

        var artifact = await _repo.FindSuccessfulPipelineArtifactByCommitAsync(
            7, "aetheus-ci", expectedCommit, "package", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(artifact);
        Assert.Equal(3, artifact.PipelineRunId);
        Assert.Null(await _repo.FindSuccessfulPipelineArtifactByCommitAsync(
            7, "aetheus-ci", expectedCommit, "failed", ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindSuccessfulPipelineArtifactByCommitAsync(
            7, "aetheus-ci", expectedCommit, "other-commit", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindReleaseArtifactAsync_LatestPublished_IgnoresNewerDraftAndFailedReleases()
    {
        _db.Projects.Add(new Project { Id = 8, Name = "Release project" });
        var artifact = MakeArtifact(projectId: 8, name: "published-package");
        await _repo.AddAsync(artifact, ct: TestContext.Current.CancellationToken);
        var published = new Release
        {
            Id = 80,
            ProjectId = 8,
            Version = "1.2.0",
            Status = ReleaseStatus.Published,
            DetectedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        published.Artifacts.Add(artifact);
        _db.Releases.AddRange(
            published,
            new Release { Id = 81, ProjectId = 8, Version = "1.3.0-draft", Status = ReleaseStatus.Detected, DetectedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) },
            new Release { Id = 82, ProjectId = 8, Version = "1.3.0-failed", Status = ReleaseStatus.Failed, DetectedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var resolved = await _repo.FindReleaseArtifactAsync(8, "latest-published", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(artifact.Id, resolved.Id);
    }

    [Fact]
    public async Task FindReleaseArtifactAsync_LatestPublished_SkipsNewerPublishedMetadataWithoutArtifact()
    {
        _db.Projects.Add(new Project { Id = 9, Name = "Rollback project" });
        var artifact = MakeArtifact(projectId: 9, name: "retained-package");
        await _repo.AddAsync(artifact, ct: TestContext.Current.CancellationToken);
        var retained = new Release
        {
            Id = 90,
            ProjectId = 9,
            Version = "1.0.0",
            Status = ReleaseStatus.Published,
            DetectedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        retained.Artifacts.Add(artifact);
        _db.Releases.AddRange(
            retained,
            new Release
            {
                Id = 91,
                ProjectId = 9,
                Version = "1.1.0",
                Status = ReleaseStatus.Published,
                DetectedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                PublishedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var resolved = await _repo.FindReleaseArtifactAsync(9, "latest-published", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(artifact.Id, resolved.Id);
    }

    [Fact]
    public async Task FindPreviousPublishedReleaseArtifactAsync_SkipsNewestArtifactFromCurrentCommit()
    {
        const string previousCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string currentCommit = "cccccccccccccccccccccccccccccccccccccccc";
        _db.Projects.Add(new Project { Id = 12, Name = "Previous release project" });
        _db.Pipelines.Add(new Pipeline { Id = 120, ProjectId = 12, Name = "aetheus-ci" });
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 120, PipelineId = 120, Status = PipelineStatus.Success, CommitHash = previousCommit },
            new PipelineRun { Id = 121, PipelineId = 120, Status = PipelineStatus.Success, CommitHash = currentCommit });
        var previousArtifact = MakeArtifact(pipelineId: 120, projectId: 12, runId: 120, name: "previous-package");
        var currentArtifact = MakeArtifact(pipelineId: 120, projectId: 12, runId: 121, name: "current-package");
        await _repo.AddAsync(previousArtifact, TestContext.Current.CancellationToken);
        await _repo.AddAsync(currentArtifact, TestContext.Current.CancellationToken);
        var previousRelease = new Release
        {
            Id = 120,
            ProjectId = 12,
            Version = "1.0.0",
            Status = ReleaseStatus.Published,
            DetectedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        previousRelease.Artifacts.Add(previousArtifact);
        var currentRelease = new Release
        {
            Id = 121,
            ProjectId = 12,
            Version = "1.1.0",
            Status = ReleaseStatus.Published,
            DetectedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            PublishedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        };
        currentRelease.Artifacts.Add(currentArtifact);
        _db.Releases.AddRange(previousRelease, currentRelease);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var resolved = await _repo.FindPreviousPublishedReleaseArtifactAsync(
            12, currentCommit, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(previousArtifact.Id, resolved.Id);
    }

    [Fact]
    public async Task FindPreviousDeployedReleaseArtifactAsync_IgnoresNewerPublishedRelease()
    {
        const string deployedCommit = "dddddddddddddddddddddddddddddddddddddddd";
        const string currentCommit = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        _db.Projects.Add(new Project { Id = 13, Name = "Active deployment project" });
        _db.Pipelines.Add(new Pipeline { Id = 130, ProjectId = 13, Name = "aetheus-ci" });
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 130, PipelineId = 130, Status = PipelineStatus.Success, CommitHash = deployedCommit },
            new PipelineRun { Id = 131, PipelineId = 130, Status = PipelineStatus.Success, CommitHash = currentCommit });
        var deployedArtifact = MakeArtifact(pipelineId: 130, projectId: 13, runId: 130, name: "deployed-package");
        var publishedArtifact = MakeArtifact(pipelineId: 130, projectId: 13, runId: 131, name: "published-package");
        await _repo.AddAsync(deployedArtifact, TestContext.Current.CancellationToken);
        await _repo.AddAsync(publishedArtifact, TestContext.Current.CancellationToken);
        var deployedRelease = new Release
        {
            Id = 130,
            ProjectId = 13,
            Version = "1.0.0",
            Status = ReleaseStatus.Deployed,
            PublishedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        deployedRelease.Artifacts.Add(deployedArtifact);
        var publishedRelease = new Release
        {
            Id = 131,
            ProjectId = 13,
            Version = "1.1.0",
            Status = ReleaseStatus.Published,
            PublishedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        };
        publishedRelease.Artifacts.Add(publishedArtifact);
        _db.Releases.AddRange(deployedRelease, publishedRelease);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var resolved = await _repo.FindPreviousDeployedReleaseArtifactAsync(
            13, currentCommit, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(deployedArtifact.Id, resolved.Id);
    }

    [Fact]
    public async Task HasPublishedRollbackContractReleaseAsync_DistinguishesOrdinaryAndContractReleases()
    {
        _db.Projects.Add(new Project { Id = 10, Name = "Contract project" });
        _db.Pipelines.AddRange(
            new Pipeline { Id = 10, ProjectId = 10, Name = "aetheus-release" },
            new Pipeline { Id = 11, ProjectId = 10, Name = "aetheus-release-with-rollback" });
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 10, PipelineId = 10, Status = PipelineStatus.Success },
            new PipelineRun { Id = 11, PipelineId = 11, Status = PipelineStatus.Success });
        _db.Releases.Add(new Release
        {
            Id = 100,
            ProjectId = 10,
            PipelineRunId = 10,
            Version = "1.0.0",
            Status = ReleaseStatus.Published
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.HasPublishedRollbackContractReleaseAsync(10, ct: TestContext.Current.CancellationToken));

        _db.Releases.Add(new Release
        {
            Id = 101,
            ProjectId = 10,
            PipelineRunId = 11,
            Version = "1.1.0",
            Status = ReleaseStatus.Published
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.HasPublishedRollbackContractReleaseAsync(10, ct: TestContext.Current.CancellationToken));

        var artifact = MakeArtifact(projectId: 10, runId: 11, name: "rollback-contract");
        await _repo.AddAsync(artifact, ct: TestContext.Current.CancellationToken);
        var contractRelease = await _db.Releases.FindAsync([101], TestContext.Current.CancellationToken);
        Assert.NotNull(contractRelease);
        contractRelease.Artifacts.Add(artifact);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.HasPublishedRollbackContractReleaseAsync(10, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetProjectRetentionOverridesAsync_ReturnsOverrides()
    {
        _db.Projects.Add(new Project { Id = 1, Name = "P", ArtifactRetentionDays = 30, ArtifactLatestRetentionDays = 90 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (defaultDays, latestDays) = await _repo.GetProjectRetentionOverridesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(30, defaultDays);
        Assert.Equal(90, latestDays);
    }

    [Fact]
    public async Task GetProjectRetentionOverridesAsync_MissingProject_ReturnsNulls()
    {
        var (defaultDays, latestDays) = await _repo.GetProjectRetentionOverridesAsync(404, ct: TestContext.Current.CancellationToken);

        Assert.Null(defaultDays);
        Assert.Null(latestDays);
    }
}
