// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Components.Releases;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ReleaseServiceTests
{
    private readonly IReleaseRepository _repoMock = Substitute.For<IReleaseRepository>();
    private readonly IProjectService _projectServiceMock = Substitute.For<IProjectService>();
    private readonly IGitCliService _gitServiceMock = Substitute.For<IGitCliService>();
    private readonly IPipelineLauncher _pipelineLauncherMock = Substitute.For<IPipelineLauncher>();
    private readonly IPipelineService _pipelineServiceMock = Substitute.For<IPipelineService>();
    private readonly IHubContext<ReleaseHub> _releaseHubMock = Substitute.For<IHubContext<ReleaseHub>>();
    private readonly IGitGraphRecorder _gitGraphMock = Substitute.For<IGitGraphRecorder>();
    private readonly IArtifactStorageService _artifactStorageMock = Substitute.For<IArtifactStorageService>();
    private readonly IArtifactRepository _artifactRepoMock = Substitute.For<IArtifactRepository>();
    private readonly IArtifactRetentionService _artifactRetentionMock = Substitute.For<IArtifactRetentionService>();
    private readonly IPipelineRepository _pipelineRepoMock = Substitute.For<IPipelineRepository>();
    private readonly IBackupRepository _backupRepoMock = Substitute.For<IBackupRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly IDbTransactionScope _transactionMock = Substitute.For<IDbTransactionScope>();
    private readonly ReleaseService _sut;

    public ReleaseServiceTests()
    {
        _sut = new ReleaseService(_repoMock, _projectServiceMock, _gitServiceMock, _pipelineLauncherMock, _pipelineServiceMock, _releaseHubMock, _gitGraphMock, TimeProvider.System, _artifactStorageMock, _backupRepoMock, _auditMock, _artifactRepoMock, _artifactRetentionMock, _pipelineRepoMock, _transactionMock);
    }

    [Fact]
    public async Task GetReleasesAsync_ReturnsMappedResult()
    {
        var releases = new List<Release>
        {
            new() { Id = 1, ProjectId = 1, Version = "1.0.0", BranchName = "release/v1.0.0", Status = ReleaseStatus.Detected, Project = new Project { Name = "App" } }
        };
        _repoMock.GetReleasesPagedAsync(null, null, 1, 10, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns((releases, 1));

        var result = await _sut.GetReleasesAsync(null, new PaginationRequest { Page = 1, PageSize = 10 }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("1.0.0", result.Items[0].Version);
    }

    [Fact]
    public async Task GetReleasesAsync_EmptyList_ReturnsEmpty()
    {
        _repoMock.GetReleasesPagedAsync(null, null, 1, 10, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns((new List<Release>(), 0));

        var result = await _sut.GetReleasesAsync(null, new PaginationRequest { Page = 1, PageSize = 10 }, ct: TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetReleaseAsync_Found_ReturnsDto()
    {
        var release = new Release { Id = 1, Version = "2.0.0", BranchName = "release/v2.0.0", Status = ReleaseStatus.Published, Project = new Project { Name = "P" } };
        _repoMock.FindReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(release);

        var result = await _sut.GetReleaseAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task GetReleaseAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindReleaseAsync(99, Arg.Any<CancellationToken>())
            .Returns((Release?)null);

        var result = await _sut.GetReleaseAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task SyncReleasesAsync_ProjectNotFound_ThrowsNotFoundException()
    {
        _projectServiceMock.GetProjectDetailAsync(99, Arg.Any<CancellationToken>())
            .Returns((ProjectDetailDto?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SyncReleasesAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncReleasesAsync_NoRepositoryUrl_ThrowsBadRequest()
    {
        _projectServiceMock.GetProjectDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ProjectDetailDto { Id = 1, RepositoryUrl = null });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SyncReleasesAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncReleasesAsync_EmptyRepositoryUrl_ThrowsBadRequest()
    {
        _projectServiceMock.GetProjectDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ProjectDetailDto { Id = 1, RepositoryUrl = "  " });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SyncReleasesAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncReleasesAsync_NewBranches_CreatesReleases()
    {
        var project = new ProjectDetailDto { Id = 1, RepositoryUrl = "https://github.com/test/repo.git" };
        _projectServiceMock.GetProjectDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(project);

        _gitServiceMock.ListReleaseBranchesAsync(project.RepositoryUrl, Arg.Any<CancellationToken>())
            .Returns([("release/v1.0.0", "1.0.0"), ("release/v2.0.0", "2.0.0")]);

        _repoMock.FindByVersionAsync(1, "1.0.0", Arg.Any<CancellationToken>())
            .Returns((Release?)null);
        _repoMock.FindByVersionAsync(1, "2.0.0", Arg.Any<CancellationToken>())
            .Returns((Release?)null);
        _repoMock.AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetProjectReleasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new Release { Id = 1, ProjectId = 1, Version = "1.0.0", BranchName = "release/v1.0.0", Status = ReleaseStatus.Detected, Project = new Project { Name = "P" } },
                new Release { Id = 2, ProjectId = 1, Version = "2.0.0", BranchName = "release/v2.0.0", Status = ReleaseStatus.Detected, Project = new Project { Name = "P" } }
            ]);

        var result = await _sut.SyncReleasesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        await _repoMock.Received(2).AddReleaseAsync(Arg.Is<Release>(rel => rel.Status == ReleaseStatus.Detected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncReleasesAsync_ExistingBranch_SkipsCreation()
    {
        var project = new ProjectDetailDto { Id = 1, RepositoryUrl = "https://github.com/test/repo.git" };
        _projectServiceMock.GetProjectDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(project);

        _gitServiceMock.ListReleaseBranchesAsync(project.RepositoryUrl, Arg.Any<CancellationToken>())
            .Returns([("release/v1.0.0", "1.0.0")]);

        _repoMock.FindByVersionAsync(1, "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new Release { Id = 1, Version = "1.0.0" }); // Already exists

        _repoMock.GetProjectReleasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new Release { Id = 1, ProjectId = 1, Version = "1.0.0", BranchName = "release/v1.0.0", Project = new Project { Name = "P" } }]);

        var result = await _sut.SyncReleasesAsync(1, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerReleaseBuildAsync_Found_SetsStatusToBuilding()
    {
        var release = new Release { Id = 1, ProjectId = 1, Status = ReleaseStatus.Detected, Project = new Project { Id = 1, Name = "P" } };
        _repoMock.FindReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _pipelineServiceMock.GetPipelineAsync(5, Arg.Any<CancellationToken>())
            .Returns(new PipelineDto { Id = 5, ProjectId = 1 });
        _pipelineLauncherMock.TriggerRunAsync(5, Arg.Any<Dictionary<string, string>?>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRunDto { Id = 100 });
        SetupHubClients();

        var result = await _sut.TriggerReleaseBuildAsync(1, new TriggerReleaseBuildRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Building, result.Status);
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerReleaseBuildAsync_NotFound_ThrowsNotFoundException()
    {
        _repoMock.FindReleaseAsync(99, Arg.Any<CancellationToken>())
            .Returns((Release?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.TriggerReleaseBuildAsync(99, new TriggerReleaseBuildRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollbackReleaseAsync_QueuesVerifiedPreviousArtifact_WithoutChangingStatusEarly()
    {
        var release = new Release { Id = 2, ProjectId = 1, Version = "2.0", Status = ReleaseStatus.Deployed, PublishedAt = DateTime.UtcNow };
        var target = new Release
        {
            Id = 1,
            ProjectId = 1,
            Version = "1.0",
            Status = ReleaseStatus.Published,
            Artifacts = [new PipelineArtifact { Id = 10, FilePath = "1/1/1/release.zip", CreatedAt = DateTime.UtcNow }]
        };
        _repoMock.FindReleaseAsync(1, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.FindPreviousPublishedWithArtifactAsync(release, Arg.Any<CancellationToken>()).Returns(target);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.AddRollbackAsync(Arg.Any<ReleaseRollback>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _artifactStorageMock.OpenArtifact("1/1/1/release.zip").Returns(new MemoryStream([1]));
        _pipelineServiceMock.GetPipelineAsync(5, Arg.Any<CancellationToken>()).Returns(new PipelineDto
        {
            Id = 5,
            ProjectId = 1,
            YamlDefinition = "name: rollback\nstages:\n  - name: Deploy\n    steps:\n      - name: deploy\n        type: deploy\n        release: $(RELEASE)"
        });
        _pipelineServiceMock.ValidateYaml(Arg.Any<string>()).Returns(new PipelineYamlDefinition
        {
            Stages = [new PipelineStageDefinition { Name = "Deploy", Steps = [new PipelineStepDefinition { Name = "deploy", Type = "deploy", Release = "$(RELEASE)" }] }]
        });
        _pipelineLauncherMock.TriggerRunAsync(5, Arg.Any<Dictionary<string, string>?>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRunDto { Id = 100 });
        SetupHubClients();

        var result = await _sut.RollbackReleaseAsync(1, new RollbackReleaseRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStatus.Pending, result.Status);
        Assert.Equal(ReleaseStatus.Deployed, release.Status);
        await _repoMock.Received(1).AddRollbackAsync(Arg.Is<ReleaseRollback>(r => r.SourceReleaseId == 2 && r.TargetReleaseId == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackReleaseAsync_SynchronouslyFailedPipeline_ClosesRollbackAsFailed()
    {
        var release = new Release { Id = 2, ProjectId = 1, Version = "2.0", Status = ReleaseStatus.Deployed, PublishedAt = DateTime.UtcNow };
        var target = new Release
        {
            Id = 1,
            ProjectId = 1,
            Version = "1.0",
            Status = ReleaseStatus.Published,
            Artifacts = [new PipelineArtifact { Id = 10, FilePath = "1/1/1/release.zip", CreatedAt = DateTime.UtcNow }]
        };
        _repoMock.FindReleaseAsync(2, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.FindPreviousPublishedWithArtifactAsync(release, Arg.Any<CancellationToken>()).Returns(target);
        _repoMock.AddRollbackAsync(Arg.Any<ReleaseRollback>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _artifactStorageMock.OpenArtifact("1/1/1/release.zip").Returns(new MemoryStream([1]));
        _pipelineServiceMock.GetPipelineAsync(5, Arg.Any<CancellationToken>()).Returns(new PipelineDto
        {
            Id = 5,
            ProjectId = 1,
            YamlDefinition = "valid"
        });
        _pipelineServiceMock.ValidateYaml("valid").Returns(new PipelineYamlDefinition
        {
            Stages = [new PipelineStageDefinition
            {
                Name = "Deploy",
                Steps = [new PipelineStepDefinition { Name = "deploy", Type = "deploy", Release = "$(RELEASE)" }]
            }]
        });
        _pipelineLauncherMock.TriggerRunAsync(5, Arg.Any<Dictionary<string, string>?>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRunDto { Id = 100, Status = PipelineStatus.Failed });

        var result = await _sut.RollbackReleaseAsync(2, new RollbackReleaseRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStatus.Failed, result.Status);
        Assert.NotNull(result.CompletedAt);
        Assert.Contains("health gate", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        await _auditMock.Received(1).LogAsync("RollbackFailed", "Release", 2,
            Arg.Is<string>(details => details.Contains("run=100", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackReleaseAsync_NotFound_ThrowsNotFoundException()
    {
        _repoMock.FindReleaseAsync(99, Arg.Any<CancellationToken>()).Returns((Release?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RollbackReleaseAsync(99, new RollbackReleaseRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollbackReleaseAsync_MissingArtifact_BlocksBeforePipelineRun()
    {
        var release = new Release { Id = 2, ProjectId = 1, Version = "2.0", Status = ReleaseStatus.Deployed, PublishedAt = DateTime.UtcNow };
        var target = new Release
        {
            Id = 1,
            ProjectId = 1,
            Version = "1.0",
            Status = ReleaseStatus.Published,
            Artifacts = [new PipelineArtifact { Id = 10, FilePath = "1/1/1/missing-release.zip", CreatedAt = DateTime.UtcNow }]
        };
        _repoMock.FindReleaseAsync(2, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.FindPreviousPublishedWithArtifactAsync(release, Arg.Any<CancellationToken>()).Returns(target);
        _artifactStorageMock.OpenArtifact("1/1/1/missing-release.zip").Returns((Stream?)null);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RollbackReleaseAsync(2, new RollbackReleaseRequest { PipelineId = 5 }, ct: TestContext.Current.CancellationToken));

        Assert.Contains("artifact", exception.Message, StringComparison.OrdinalIgnoreCase);
        await _pipelineLauncherMock.DidNotReceive().TriggerRunAsync(
            Arg.Any<int>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackReleaseAsync_UnverifiedDatabaseBackup_BlocksBeforePipelineRun()
    {
        var release = new Release { Id = 2, ProjectId = 1, Version = "2.0", Status = ReleaseStatus.Deployed, PublishedAt = DateTime.UtcNow };
        var target = new Release
        {
            Id = 1,
            ProjectId = 1,
            Version = "1.0",
            Status = ReleaseStatus.Published,
            Artifacts = [new PipelineArtifact { Id = 10, FilePath = "1/1/1/release.zip", CreatedAt = DateTime.UtcNow }]
        };
        _repoMock.FindReleaseAsync(2, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.FindPreviousPublishedWithArtifactAsync(release, Arg.Any<CancellationToken>()).Returns(target);
        _artifactStorageMock.OpenArtifact("1/1/1/release.zip").Returns(new MemoryStream([1]));
        _pipelineServiceMock.GetPipelineAsync(5, Arg.Any<CancellationToken>()).Returns(new PipelineDto { Id = 5, ProjectId = 1, YamlDefinition = "valid" });
        _pipelineServiceMock.ValidateYaml("valid").Returns(new PipelineYamlDefinition
        {
            Stages = [new PipelineStageDefinition
            {
                Name = "Rollback",
                Steps =
                [
                    new PipelineStepDefinition { Name = "restore", Type = "restore-backup", BackupRun = "$(AETHEUS_ROLLBACK_BACKUP_RUN_ID)" },
                    new PipelineStepDefinition { Name = "deploy", Type = "deploy", Release = "$(RELEASE)" }
                ]
            }]
        });
        _backupRepoMock.FindRunWithPolicyAsync(7, Arg.Any<CancellationToken>()).Returns(new BackupRun
        {
            Id = 7,
            Status = BackupRunStatus.Succeeded,
            RestoreCheckStatus = RestoreCheckStatus.Failed,
            ArchivePath = "/backups/7.dump",
            BackupPolicy = new BackupPolicy { ProjectId = 1 }
        });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RollbackReleaseAsync(2,
            new RollbackReleaseRequest { PipelineId = 5, RestoreDatabase = true, BackupRunId = 7 }, ct: TestContext.Current.CancellationToken));

        await _pipelineLauncherMock.DidNotReceive().TriggerRunAsync(
            Arg.Any<int>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyRollbackDeploymentSucceededAsync_UpdatesBothReleasesAndRollback()
    {
        var source = new Release { Id = 2, ProjectId = 1, Version = "2.0", Status = ReleaseStatus.Deployed, Project = new Project { Id = 1, Name = "App" } };
        var target = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Published, Project = new Project { Id = 1, Name = "App" } };
        var rollback = new ReleaseRollback { Id = 5, SourceRelease = source, TargetRelease = target, SourceReleaseId = 2, TargetReleaseId = 1 };
        _repoMock.FindRollbackAsync(5, Arg.Any<CancellationToken>()).Returns(rollback);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        SetupHubClients();

        await _sut.NotifyRollbackDeploymentSucceededAsync(5, ct: TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStatus.Succeeded, rollback.Status);
        Assert.Equal(ReleaseStatus.RolledBack, source.Status);
        Assert.Equal(ReleaseStatus.Deployed, target.Status);
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReleaseFromPipelineAsync_LinksArtifactsAlreadyCollectedForTheRun()
    {
        var artifact = new PipelineArtifact { Id = 12, PipelineRunId = 42 };
        _repoMock.FindByVersionAsync(1, "1.0.0", Arg.Any<CancellationToken>()).Returns((Release?)null);
        _repoMock.GetMaxBuildNumberAsync(1, Arg.Any<CancellationToken>()).Returns(0);
        _repoMock.AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<Release>().Id = 7;
            return Task.CompletedTask;
        });
        _artifactRepoMock.GetByRunAsync(42, Arg.Any<CancellationToken>()).Returns([artifact]);
        SetupHubClients();

        await _sut.CreateReleaseFromPipelineAsync(1, 42, "1.0.0", null, ct: TestContext.Current.CancellationToken);

        await _artifactRetentionMock.Received(1).ApplyReleaseRetentionAsync(artifact, 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReleaseFromPipelineAsync_Deployed_DemotesPreviousActiveRelease()
    {
        var previous = new Release { Id = 6, ProjectId = 1, Status = ReleaseStatus.Deployed };
        Release? created = null;
        _repoMock.GetDeployedProjectReleasesAsync(1, Arg.Any<CancellationToken>()).Returns([previous]);
        _repoMock.FindByVersionAsync(1, "1.1.0", Arg.Any<CancellationToken>()).Returns((Release?)null);
        _repoMock.GetMaxBuildNumberAsync(1, Arg.Any<CancellationToken>()).Returns(1);
        _repoMock.AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            created = call.Arg<Release>();
            created.Id = 7;
            return Task.CompletedTask;
        });
        _artifactRepoMock.GetByRunAsync(43, Arg.Any<CancellationToken>()).Returns([]);
        _transactionMock.IsRelational.Returns(true);
        SetupHubClients();

        await _sut.CreateReleaseFromPipelineAsync(
            1, 43, "1.1.0", null, deployed: true, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Published, previous.Status);
        Assert.Equal(ReleaseStatus.Deployed, created!.Status);
        await _transactionMock.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _transactionMock.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _transactionMock.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReleaseFromPipelineAsync_LinksValidatedArtifactsFromCiRun()
    {
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var sourcePipeline = new Pipeline { Id = 2, ProjectId = 1, Name = "aetheus-ci" };
        var artifact = new PipelineArtifact { Id = 12, PipelineRunId = 42, ProjectId = 1 };
        _pipelineRepoMock.GetPipelineRunWithPipelineAsync(42, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 42,
            PipelineId = 2,
            CommitHash = commit,
            Status = PipelineStatus.Success,
            Pipeline = sourcePipeline
        });
        _pipelineRepoMock.GetPipelineProjectIdAsync(sourcePipeline, Arg.Any<CancellationToken>()).Returns(1);
        _artifactRepoMock.GetByRunAsync(42, Arg.Any<CancellationToken>()).Returns([artifact]);
        _repoMock.FindByVersionAsync(1, "1.0.0", Arg.Any<CancellationToken>()).Returns((Release?)null);
        _repoMock.GetMaxBuildNumberAsync(1, Arg.Any<CancellationToken>()).Returns(0);
        _repoMock.AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<Release>().Id = 7;
            return Task.CompletedTask;
        });
        SetupHubClients();

        var release = await _sut.CreateReleaseFromPipelineAsync(
            1, 99, "1.0.0", null, commitHash: commit, branchName: "main", artifactPipelineRunId: 42, ct: TestContext.Current.CancellationToken);

        Assert.Equal(99, release.PipelineRunId);
        await _artifactRetentionMock.Received(1).ApplyReleaseRetentionAsync(
            artifact, 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReleaseFromPipelineAsync_RejectsArtifactRunAtDifferentCommit()
    {
        var sourcePipeline = new Pipeline { Id = 2, ProjectId = 1, Name = "aetheus-ci" };
        _pipelineRepoMock.GetPipelineRunWithPipelineAsync(42, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 42,
            PipelineId = 2,
            CommitHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Status = PipelineStatus.Success,
            Pipeline = sourcePipeline
        });
        _pipelineRepoMock.GetPipelineProjectIdAsync(sourcePipeline, Arg.Any<CancellationToken>()).Returns(1);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateReleaseFromPipelineAsync(
            1, 99, "1.0.0", null,
            commitHash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", artifactPipelineRunId: 42, ct: TestContext.Current.CancellationToken));

        await _repoMock.DidNotReceive().AddReleaseAsync(Arg.Any<Release>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteReleaseAsync_Found_SetsStatusToPromoted()
    {
        var release = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Published };
        _repoMock.FindReleaseAsync(1, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        SetupHubClients();

        var result = await _sut.PromoteReleaseAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Promoted, release.Status);
        Assert.NotNull(release.PromotedAt);
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteReleaseAsync_NotFound_ThrowsNotFoundException()
    {
        _repoMock.FindReleaseAsync(99, Arg.Any<CancellationToken>()).Returns((Release?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PromoteReleaseAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateWebhookSignature_NullOrEmpty_ReturnsFalse(string? signature)
    {
        Assert.False(_sut.ValidateWebhookSignature(signature, "secret", "body"));
    }

    [Fact]
    public void ValidateWebhookSignature_GitLabToken_MatchesPlainToken()
    {
        Assert.True(_sut.ValidateWebhookSignature("my-secret-token", "my-secret-token", "body"));
    }

    [Fact]
    public void ValidateWebhookSignature_GitLabToken_MismatchReturnsFalse()
    {
        Assert.False(_sut.ValidateWebhookSignature("wrong-token", "my-secret-token", "body"));
    }

    [Fact]
    public void ValidateWebhookSignature_GitHubSha256_ValidSignature()
    {
        var secret = "test-secret";
        var body = "test-body";
        var hmac = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(body));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac);

        Assert.True(_sut.ValidateWebhookSignature(signature, secret, body));
    }

    [Fact]
    public void ValidateWebhookSignature_GitHubSha256_InvalidSignature()
    {
        Assert.False(_sut.ValidateWebhookSignature("sha256=0000", "secret", "body"));
    }

    [Fact]
    public async Task NotifyPipelineRunCompletedAsync_Success_SetsPublished()
    {
        var release = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Building };
        _repoMock.FindByPipelineRunIdAsync(42, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.NotifyPipelineRunCompletedAsync(42, PipelineStatus.Success, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Published, release.Status);
        Assert.NotNull(release.PublishedAt);
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPipelineRunCompletedAsync_Success_PreservesDeployedStatus()
    {
        var release = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Deployed };
        _repoMock.FindByPipelineRunIdAsync(42, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.NotifyPipelineRunCompletedAsync(42, PipelineStatus.Success, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Deployed, release.Status);
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPipelineRunCompletedAsync_Failure_SetsFailed()
    {
        var release = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Building };
        _repoMock.FindByPipelineRunIdAsync(42, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.NotifyPipelineRunCompletedAsync(42, PipelineStatus.Failed, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Failed, release.Status);
    }

    [Fact]
    public async Task NotifyPipelineRunCompletedAsync_Failure_PreservesFactuallyDeployedStatus()
    {
        var release = new Release { Id = 1, ProjectId = 1, Version = "1.0", Status = ReleaseStatus.Deployed };
        _repoMock.FindByPipelineRunIdAsync(42, Arg.Any<CancellationToken>()).Returns(release);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.NotifyPipelineRunCompletedAsync(42, PipelineStatus.Failed, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseStatus.Deployed, release.Status);
    }

    [Fact]
    public async Task NotifyPipelineRunCompletedAsync_NoRelease_DoesNothing()
    {
        _repoMock.FindByPipelineRunIdAsync(99, Arg.Any<CancellationToken>()).Returns((Release?)null);

        await _sut.NotifyPipelineRunCompletedAsync(99, PipelineStatus.Success, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleWebhookAsync_EmptyRepoUrl_DoesNothing()
    {
        await _sut.HandleWebhookAsync(new WebhookPayload { RepositoryUrl = "" }, ct: TestContext.Current.CancellationToken);

        await _projectServiceMock.DidNotReceive().GetProjectsWithRepoUrlAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleWebhookAsync_NoMatchingProject_DoesNothing()
    {
        _projectServiceMock.GetProjectsWithRepoUrlAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProjectDto> { new() { Id = 1, Name = "P", RepositoryUrl = "https://github.com/other" } });

        await _sut.HandleWebhookAsync(new WebhookPayload { RepositoryUrl = "https://github.com/nomatch" }, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void SetupHubClients()
    {
        var mockClients = Substitute.For<IHubClients>();
        var mockClientProxy = Substitute.For<IClientProxy>();
        mockClients.Group(Arg.Any<string>()).Returns(mockClientProxy);
        mockClients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(mockClientProxy);
        _releaseHubMock.Clients.Returns(mockClients);
    }
}
