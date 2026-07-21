// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Releases;

public class ReleaseService(
    IReleaseRepository repo,
    IProjectService projectService,
    IGitCliService gitService,
    IPipelineLauncher pipelineLauncher,
    IPipelineService pipelineService,
    IHubContext<ReleaseHub> releaseHub,
    IGitGraphRecorder gitGraph,
    TimeProvider timeProvider,
    IArtifactStorageService? artifactStorage = null,
    IBackupRepository? backupRepo = null,
    IAuditService? audit = null,
    IArtifactRepository? artifactRepo = null,
    IArtifactRetentionService? artifactRetention = null,
    IPipelineRepository? pipelineRepo = null,
    IDbTransactionScope? transaction = null) : IReleaseService
{
    public async Task<PaginatedResult<ReleaseDto>> GetReleasesAsync(int? projectId, PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetReleasesPagedAsync(
            request.Search, projectId, page, pageSize, accessibleIds, ct).ConfigureAwait(false);

        return new PaginatedResult<ReleaseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ReleaseDto?> GetReleaseAsync(int id, CancellationToken ct = default)
    {
        var release = await repo.FindReleaseAsync(id, ct).ConfigureAwait(false);
        return release is null ? null : MapToDto(release);
    }

    public async Task<ReleaseRollbackPreviewDto> GetRollbackPreviewAsync(int id, CancellationToken ct = default)
    {
        var release = await repo.FindReleaseAsync(id, ct).ConfigureAwait(false);
        if (release is null) throw new NotFoundException("Release not found");
        var target = await repo.FindPreviousPublishedWithArtifactAsync(release, ct).ConfigureAwait(false);
        if (target is null)
            return new ReleaseRollbackPreviewDto { Reason = "No previous release with a retained artifact is available." };

        var artifact = target.Artifacts.OrderByDescending(a => a.CreatedAt).First();
        await using var probe = artifactStorage?.OpenArtifact(artifact.FilePath);
        return probe is null
            ? new ReleaseRollbackPreviewDto { Reason = "The previous release artifact is no longer available in storage." }
            : new ReleaseRollbackPreviewDto
            {
                CanRollback = true,
                TargetVersion = target.Version,
                TargetPublishedAt = target.PublishedAt
            };
    }

    public async Task<List<ReleaseDto>> GetReleasesByRunAsync(int pipelineRunId, CancellationToken ct = default)
    {
        var releases = await repo.GetByPipelineRunIdAsync(pipelineRunId, ct).ConfigureAwait(false);
        return releases.Select(MapToDto).ToList();
    }

    public async Task<List<ReleaseDto>> SyncReleasesAsync(int projectId, CancellationToken ct = default)
    {
        await SyncReleaseRowsAsync(projectId, ct).ConfigureAwait(false);
        var releases = await repo.GetProjectReleasesAsync(projectId, ct).ConfigureAwait(false);
        return releases.Select(MapToDto).ToList();
    }

    private async Task SyncReleaseRowsAsync(int projectId, CancellationToken ct)
    {
        var project = await projectService.GetProjectDetailAsync(projectId, ct).ConfigureAwait(false);
        if (project is null) throw new NotFoundException("Project not found");
        if (string.IsNullOrWhiteSpace(project.RepositoryUrl))
            throw new BadRequestException("Project has no repository URL");

        var branches = await gitService.ListReleaseBranchesAsync(project.RepositoryUrl, ct).ConfigureAwait(false);

        foreach (var (branchName, version) in branches)
        {
            var existing = await repo.FindByVersionAsync(projectId, version, ct).ConfigureAwait(false);
            if (existing is not null) continue;

            await repo.AddReleaseAsync(new Release
            {
                ProjectId = projectId,
                Version = version,
                BranchName = branchName,
                Status = ReleaseStatus.Detected
            }, ct).ConfigureAwait(false);
        }
    }

    public async Task<ReleaseDto> TriggerReleaseBuildAsync(int releaseId, TriggerReleaseBuildRequest request, CancellationToken ct = default)
    {
        var release = await repo.FindReleaseAsync(releaseId, ct).ConfigureAwait(false);
        if (release is null) throw new NotFoundException("Release not found");

        // F-12: ensure the requested pipeline belongs to the same project as the release. Resolved
        // through the Pipelines service contract (not its repository) - cross-module deps via interfaces.
        var pipeline = await pipelineService.GetPipelineAsync(request.PipelineId, ct).ConfigureAwait(false);
        if (pipeline is null) throw new NotFoundException("Pipeline not found");
        if (pipeline.ProjectId != release.ProjectId)
            throw new BadRequestException("Pipeline does not belong to the release's project.");

        release.Status = ReleaseStatus.Building;

        var runResult = await pipelineLauncher.TriggerRunAsync(request.PipelineId, ct: ct).ConfigureAwait(false);
        if (runResult is not null)
            release.PipelineRunId = runResult.Id;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapToDto(release);
    }

    public async Task<ReleaseRollbackDto> RollbackReleaseAsync(int releaseId, RollbackReleaseRequest request, CancellationToken ct = default)
    {
        var release = await repo.FindReleaseAsync(releaseId, ct).ConfigureAwait(false);
        if (release is null) throw new NotFoundException("Release not found");

        var target = await repo.FindPreviousPublishedWithArtifactAsync(release, ct).ConfigureAwait(false);
        if (target is null)
            throw new BadRequestException("No previous release with a retained artifact is available for rollback.");

        // The database row is not proof that the blob is still retained. Fail before creating a run or
        // dispatching a task: a rollback without bytes must never become a deceptive green request.
        var artifact = target.Artifacts.OrderByDescending(a => a.CreatedAt).First();
        await using var artifactProbe = artifactStorage?.OpenArtifact(artifact.FilePath);
        if (artifactProbe is null)
            throw new BadRequestException("The previous release artifact is no longer available in storage; rollback was not started.");

        var pipeline = await pipelineService.GetPipelineAsync(request.PipelineId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Pipeline not found");
        if (pipeline.ProjectId != release.ProjectId)
            throw new BadRequestException("Rollback pipeline does not belong to the release project.");

        var definition = pipelineService.ValidateYaml(pipeline.YamlDefinition)
            ?? throw new BadRequestException("Rollback pipeline YAML is invalid.");
        var rollbackDeploy = definition.Stages.SelectMany(s => s.Steps)
            .Any(s => string.Equals(s.Type, "deploy", StringComparison.OrdinalIgnoreCase)
                && IsRollbackReleaseSelector(s.Release));
        if (!rollbackDeploy)
            throw new BadRequestException("Rollback pipeline must contain a type: deploy step using $(RELEASE) or $(ROLLBACK_RELEASE).");

        BackupRun? backup = null;
        if (request.RestoreDatabase)
        {
            if (request.BackupRunId is null)
                throw new BadRequestException("A verified backup is required when database restore is requested.");
            backup = backupRepo is null ? null : await backupRepo.FindRunWithPolicyAsync(request.BackupRunId.Value, ct).ConfigureAwait(false);
            if (backup?.BackupPolicy.ProjectId != release.ProjectId
                || backup.Status != BackupRunStatus.Succeeded
                || backup.RestoreCheckStatus != RestoreCheckStatus.Verified
                || string.IsNullOrWhiteSpace(backup.ArchivePath))
                throw new BadRequestException("The selected backup is not a verified successful backup for this project.");

            var rollbackRestore = definition.Stages.SelectMany(s => s.Steps)
                .Any(s => string.Equals(s.Type, "restore-backup", StringComparison.OrdinalIgnoreCase)
                    && IsRollbackBackupSelector(s.BackupRun));
            if (!rollbackRestore)
                throw new BadRequestException("Rollback pipeline must contain a type: restore-backup step using $(AETHEUS_ROLLBACK_BACKUP_RUN_ID).");
        }

        var rollback = new ReleaseRollback
        {
            SourceReleaseId = release.Id,
            TargetReleaseId = target.Id,
            PipelineId = pipeline.Id,
            BackupRunId = backup?.Id,
            RestoreDatabase = request.RestoreDatabase,
            RequestedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        await repo.AddRollbackAsync(rollback, ct).ConfigureAwait(false);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RELEASE"] = target.Id.ToString(),
            ["ROLLBACK_RELEASE"] = target.Id.ToString(),
            ["AETHEUS_ROLLBACK_ID"] = rollback.Id.ToString(),
            ["AETHEUS_ROLLBACK_SOURCE_RELEASE_ID"] = release.Id.ToString(),
            ["AETHEUS_ROLLBACK_RESTORE_DATABASE"] = request.RestoreDatabase ? "true" : "false"
        };
        if (backup is not null)
            variables["AETHEUS_ROLLBACK_BACKUP_RUN_ID"] = backup.Id.ToString();

        var run = await pipelineLauncher.TriggerRunAsync(pipeline.Id, variables, ct: ct).ConfigureAwait(false);
        if (run is null)
        {
            rollback.Status = RollbackStatus.Failed;
            rollback.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            rollback.FailureReason = "Rollback pipeline could not be created.";
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new BadRequestException(rollback.FailureReason);
        }

        rollback.PipelineRunId = run.Id;
        if (run.Status is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled)
        {
            // A pipeline can fail synchronously during initial dispatch, before TriggerRunAsync returns.
            // In that ordering the completion event cannot find this rollback yet because PipelineRunId
            // is assigned here. Close it explicitly instead of leaving a permanently Pending record.
            rollback.Status = RollbackStatus.Failed;
            rollback.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            rollback.FailureReason = BuildRollbackFailureReason(run.Status);
        }
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        if (audit is not null) await audit.LogAsync("RollbackRequested", "Release", release.Id,
            $"from={release.Version}; to={target.Version}; run={run.Id}; restoreDatabase={request.RestoreDatabase}", ct).ConfigureAwait(false);
        if (audit is not null && rollback.Status == RollbackStatus.Failed)
            await audit.LogAsync("RollbackFailed", "Release", release.Id,
                $"from={release.Version}; to={target.Version}; run={run.Id}; reason={rollback.FailureReason}", ct).ConfigureAwait(false);
        return MapRollback(rollback);
    }

    public async Task<ReleaseDto> PromoteReleaseAsync(int releaseId, CancellationToken ct = default)
    {
        var release = await repo.FindReleaseAsync(releaseId, ct).ConfigureAwait(false);
        if (release is null) throw new NotFoundException("Release not found");

        release.Status = ReleaseStatus.Promoted;
        release.PromotedAt = timeProvider.GetUtcNow().UtcDateTime;
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var dto = MapToDto(release);
        await releaseHub.Clients.Groups([HubGroups.AllReleases, HubGroups.ProjectReleases(dto.ProjectId)])
            .SendAsync("ReleaseStatusChanged", dto, ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<ReleaseDto> CreateReleaseFromPipelineAsync(
        int projectId, int pipelineRunId, string version, string? changelog,
        string? commitHash = null, string? tagName = null, string? branchName = null,
        int? artifactPipelineRunId = null, bool deployed = false, CancellationToken ct = default)
    {
        var retainedArtifactRunId = artifactPipelineRunId ?? pipelineRunId;
        IReadOnlyList<PipelineArtifact>? validatedArtifacts = null;
        if (retainedArtifactRunId != pipelineRunId)
            validatedArtifacts = await ValidateArtifactSourceRunAsync(
                projectId, retainedArtifactRunId, commitHash, ct).ConfigureAwait(false);

        if (artifactPipelineRunId.HasValue && artifactRetention is null)
            throw new BadRequestException("Artifact retention is unavailable; refusing to publish a release without a retained payload.");

        async Task<(ReleaseDto Dto, bool Created)> PersistAsync()
        {
            var deployedReleases = deployed
                ? await repo.GetDeployedProjectReleasesAsync(projectId, ct).ConfigureAwait(false)
                : [];
            foreach (var deployedRelease in deployedReleases)
                deployedRelease.Status = ReleaseStatus.Published;
            if (deployedReleases.Count > 0)
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            var releaseStatus = deployed ? ReleaseStatus.Deployed : ReleaseStatus.Published;
            var existingByVersion = await repo.FindByVersionAsync(projectId, version, ct).ConfigureAwait(false);
            if (existingByVersion is not null)
            {
                existingByVersion.Status = releaseStatus;
                existingByVersion.PublishedAt = timeProvider.GetUtcNow().UtcDateTime;
                existingByVersion.PipelineRunId = pipelineRunId;
                existingByVersion.Changelog = changelog;
                if (commitHash is not null) existingByVersion.CommitHash = commitHash;
                if (tagName is not null) existingByVersion.TagName = tagName;
                if (branchName is not null) existingByVersion.BranchName = branchName;
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                await LinkRunArtifactsToReleaseAsync(
                    existingByVersion, retainedArtifactRunId, validatedArtifacts, ct).ConfigureAwait(false);
                await gitGraph.RecordReleaseContextAsync(existingByVersion, ct).ConfigureAwait(false);
                return (MapToDto(existingByVersion), false);
            }

            var maxBuildNumber = await repo.GetMaxBuildNumberAsync(projectId, ct).ConfigureAwait(false);
            var release = new Release
            {
                ProjectId = projectId,
                Version = version,
                BranchName = branchName ?? string.Empty,
                Status = releaseStatus,
                PipelineRunId = pipelineRunId,
                Changelog = changelog,
                CommitHash = commitHash,
                TagName = tagName ?? $"v{version}",
                BuildNumber = maxBuildNumber + 1,
                PublishedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            await repo.AddReleaseAsync(release, ct).ConfigureAwait(false);
            await LinkRunArtifactsToReleaseAsync(
                release, retainedArtifactRunId, validatedArtifacts, ct).ConfigureAwait(false);
            await gitGraph.RecordReleaseContextAsync(release, ct).ConfigureAwait(false);
            return (MapToDto(release), true);
        }

        (ReleaseDto Dto, bool Created) persisted;
        if (transaction?.IsRelational == true)
        {
            await transaction.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                persisted = await PersistAsync().ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            persisted = await PersistAsync().ConfigureAwait(false);
        }

        if (persisted.Created)
            await releaseHub.Clients.Groups([HubGroups.AllReleases, HubGroups.ProjectReleases(projectId)])
                .SendAsync("ReleaseCreated", persisted.Dto, ct).ConfigureAwait(false);
        return persisted.Dto;
    }

    public async Task HandleWebhookAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payload.RepositoryUrl)) return;

        var projects = await projectService.GetProjectsWithRepoUrlAsync(ct).ConfigureAwait(false);
        var normalizedPayloadUrl = RepositoryUrlNormalizer.Normalize(payload.RepositoryUrl);
        var matchingProject = projects.FirstOrDefault(p =>
            RepositoryUrlNormalizer.Normalize(p.RepositoryUrl!).Equals(normalizedPayloadUrl, StringComparison.OrdinalIgnoreCase));

        if (matchingProject is null) return;

        // The webhook only needs to persist newly detected rows. Avoid materializing the project's
        // complete release history and its linked artifacts when nobody consumes the returned list.
        await SyncReleaseRowsAsync(matchingProject.Id, ct).ConfigureAwait(false);
    }

    public bool ValidateWebhookSignature(string? signature, string secret, string rawBody) =>
        WebhookSignatureValidator.Validate(signature, secret, rawBody);

    public async Task NotifyPipelineRunCompletedAsync(int pipelineRunId, PipelineStatus status, CancellationToken ct = default)
    {
        var rollback = await repo.FindRollbackByPipelineRunIdAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (rollback is not null && rollback.Status == RollbackStatus.Pending)
        {
            rollback.Status = RollbackStatus.Failed;
            rollback.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            rollback.FailureReason = BuildRollbackFailureReason(status);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            if (audit is not null) await audit.LogAsync("RollbackFailed", "Release", rollback.SourceReleaseId,
                $"from={rollback.SourceRelease.Version}; to={rollback.TargetRelease.Version}; run={pipelineRunId}; reason={rollback.FailureReason}", ct).ConfigureAwait(false);
            return;
        }

        var release = await repo.FindByPipelineRunIdAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (release is null) return;

        release.Status = release.Status == ReleaseStatus.Deployed
            ? ReleaseStatus.Deployed
            : status == PipelineStatus.Success ? ReleaseStatus.Published : ReleaseStatus.Failed;
        if (release.Status == ReleaseStatus.Published)
            release.PublishedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task NotifyRollbackDeploymentSucceededAsync(int rollbackId, CancellationToken ct = default)
    {
        var rollback = await repo.FindRollbackAsync(rollbackId, ct).ConfigureAwait(false);
        if (rollback is null || rollback.Status != RollbackStatus.Pending) return;

        rollback.Status = RollbackStatus.Succeeded;
        rollback.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        rollback.SourceRelease.Status = ReleaseStatus.RolledBack;
        rollback.SourceRelease.RolledBackAt = rollback.CompletedAt;
        rollback.TargetRelease.Status = ReleaseStatus.Deployed;
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        if (audit is not null) await audit.LogAsync("RollbackSucceeded", "Release", rollback.SourceReleaseId,
            $"from={rollback.SourceRelease.Version}; to={rollback.TargetRelease.Version}; run={rollback.PipelineRunId}; restoreDatabase={rollback.RestoreDatabase}", ct).ConfigureAwait(false);
        await BroadcastStatusAsync(rollback.SourceRelease, ct).ConfigureAwait(false);
        await BroadcastStatusAsync(rollback.TargetRelease, ct).ConfigureAwait(false);
    }

    private async Task BroadcastStatusAsync(Release release, CancellationToken ct)
    {
        var dto = MapToDto(release);
        await releaseHub.Clients.Groups([HubGroups.AllReleases, HubGroups.ProjectReleases(dto.ProjectId)])
            .SendAsync("ReleaseStatusChanged", dto, ct).ConfigureAwait(false);
    }

    private static bool IsRollbackReleaseSelector(string? selector) =>
        string.Equals(selector, "$(RELEASE)", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "$(ROLLBACK_RELEASE)", StringComparison.OrdinalIgnoreCase);

    private static bool IsRollbackBackupSelector(string? selector) =>
        string.Equals(selector, "$(AETHEUS_ROLLBACK_BACKUP_RUN_ID)", StringComparison.OrdinalIgnoreCase);

    private static string BuildRollbackFailureReason(PipelineStatus status) =>
        status == PipelineStatus.Success
            ? "Rollback pipeline completed without a successful deployment task."
            : "Rollback pipeline failed before the deployment health gate succeeded.";

    private async Task<IReadOnlyList<PipelineArtifact>> ValidateArtifactSourceRunAsync(
        int projectId, int artifactPipelineRunId, string? commitHash, CancellationToken ct)
    {
        if (pipelineRepo is null || artifactRepo is null)
            throw new BadRequestException("Artifact source validation is unavailable.");

        var sourceRun = await pipelineRepo.GetPipelineRunWithPipelineAsync(
            artifactPipelineRunId, ct).ConfigureAwait(false);
        var sourceProjectId = sourceRun?.Pipeline is null
            ? null
            : await pipelineRepo.GetPipelineProjectIdAsync(sourceRun.Pipeline, ct).ConfigureAwait(false);
        if (sourceProjectId != projectId
            || sourceRun?.Status != PipelineStatus.Success
            || string.IsNullOrWhiteSpace(commitHash)
            || !string.Equals(sourceRun.CommitHash, commitHash, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("The artifact source run is not a successful same-project run at the release commit.");

        var artifacts = await artifactRepo.GetByRunAsync(artifactPipelineRunId, ct).ConfigureAwait(false);
        if (artifacts.Count == 0 || artifacts.Any(artifact => artifact.ProjectId != projectId))
            throw new BadRequestException("The artifact source run has no releasable artifact for this project.");
        return artifacts;
    }

    private async Task LinkRunArtifactsToReleaseAsync(
        Release release, int artifactPipelineRunId, IReadOnlyList<PipelineArtifact>? validatedArtifacts,
        CancellationToken ct)
    {
        if (artifactRepo is null || artifactRetention is null)
            return;

        var artifacts = validatedArtifacts
            ?? await artifactRepo.GetByRunAsync(artifactPipelineRunId, ct).ConfigureAwait(false);
        foreach (var artifact in artifacts)
            await artifactRetention.ApplyReleaseRetentionAsync(artifact, release.Id, ct).ConfigureAwait(false);
    }

    private static ReleaseRollbackDto MapRollback(ReleaseRollback rollback) => new()
    {
        Id = rollback.Id,
        SourceReleaseId = rollback.SourceReleaseId,
        TargetReleaseId = rollback.TargetReleaseId,
        PipelineRunId = rollback.PipelineRunId,
        BackupRunId = rollback.BackupRunId,
        RestoreDatabase = rollback.RestoreDatabase,
        Status = rollback.Status,
        RequestedAt = rollback.RequestedAt,
        CompletedAt = rollback.CompletedAt,
        FailureReason = rollback.FailureReason
    };

    private static ReleaseDto MapToDto(Release r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        ProjectName = r.Project?.Name ?? string.Empty,
        Version = r.Version,
        BranchName = r.BranchName,
        Status = r.Status,
        DetectedAt = r.DetectedAt,
        PublishedAt = r.PublishedAt,
        PromotedAt = r.PromotedAt,
        RolledBackAt = r.RolledBackAt,
        PipelineRunId = r.PipelineRunId,
        Changelog = r.Changelog,
        BuildNumber = r.BuildNumber,
        CommitHash = r.CommitHash,
        TagName = r.TagName,
        RepositoryUrl = r.Project?.RepositoryUrl,
        Artifacts = r.Artifacts.Select(GitGraphMapper.ToLink).ToList(),
        Commits = r.Commits.Select(GitGraphMapper.ToLink).ToList(),
        Branches = r.Branches.Select(GitGraphMapper.ToLink).ToList()
    };
}
