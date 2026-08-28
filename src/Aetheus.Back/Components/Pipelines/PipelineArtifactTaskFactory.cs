// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.Json;
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Helpers;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// The two steps that bring something back onto the runner before the run can use it: restoring
/// pipeline artifacts, and restoring an application backup.
/// </summary>
public interface IPipelineArtifactTaskFactory
{
    Task CreateRestoreArtifactsTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct);

    Task CreateBackupRestoreTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct);

    /// <summary>
    /// Resolves the artifact a step points at - same run, another pipeline's run, or a release.
    /// Public because the release step resolves the same thing and must not grow its own version of it.
    /// </summary>
    Task<(PipelineArtifact? Artifact, string? Error)> ResolveArtifactSourceAsync(
        int runId, Dictionary<string, string> variables, PipelineStepDefinition stepDef,
        CancellationToken ct);
}

/// <summary>
/// Resolves what a restore step needs and dispatches it. Unlike the other factories this one may also
/// legitimately COMPLETE a step without dispatching anything: an optional producer that never ran
/// leaves nothing to restore, and saying so is the honest outcome rather than a failure. What it
/// never does is advance or finalize the run.
/// </summary>
public sealed class PipelineArtifactTaskFactory(
    IPipelineRepository repo,
    IArtifactRepository artifactRepo,
    IEncryptionService encryption,
    TimeProvider timeProvider,
    IBackupRepository? backupRepo = null) : IPipelineArtifactTaskFactory
{
    public async Task CreateRestoreArtifactsTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var releaseSelector = SubstituteVariables(stepDef.Release ?? string.Empty, legVars).Trim();
        var (artifact, error) = await ResolveArtifactSourceAsync(runId, legVars, stepDef, ct).ConfigureAwait(false);
        if (artifact is null)
        {
            await HandleMissingRestoreArtifactAsync(
                runId, legServer, stepRun, stepDef, releaseSelector, error, now, ct).ConfigureAwait(false);
            return;
        }
        var targetDirectory = SubstituteVariables(stepDef.TargetDirectory ?? string.Empty, legVars).Trim();
        if (!PipelinePathValidation.IsSafeRelativeDirectory(targetDirectory))
        {
            await FailRestoreArtifactsAsync(
                runId, legServer, stepRun, "target_directory must be a safe relative path.", now, ct)
                .ConfigureAwait(false);
            return;
        }
        if (artifact.Sha256 is not { Length: 64 } artifactSha256
            || artifactSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            await FailRestoreArtifactsAsync(
                runId, legServer, stepRun,
                $"artifact '{artifact.Name}' has no valid authoritative SHA-256.", now, ct).ConfigureAwait(false);
            return;
        }
        CreateRestoreArtifactsTask(
            runId, legServer, stepRun, stepDef, legVars, artifact, artifactSha256,
            releaseSelector, targetDirectory);
    }

    private async Task HandleMissingRestoreArtifactAsync(
        int runId,
        Server server,
        PipelineStepRun stepRun,
        PipelineStepDefinition stepDefinition,
        string releaseSelector,
        string? error,
        DateTime now,
        CancellationToken ct)
    {
        if (stepDefinition.AllowMissing && IsOptionalProducerArtifactUnavailable(stepDefinition, error))
        {
            await CompleteMissingRestoreArtifactAsync(
                runId, server, stepRun,
                $"optional producer artifact is unavailable; downstream assurance will record Unavailable/F. {error}",
                now, ct).ConfigureAwait(false);
            return;
        }
        var canBootstrap = stepDefinition.AllowMissing
            && IsBootstrapReleaseSelector(releaseSelector)
            && error?.Contains("has no retained artifact", StringComparison.Ordinal) == true;
        if (!canBootstrap)
        {
            await FailRestoreArtifactsAsync(
                runId, server, stepRun, error ?? "the requested artifact could not be resolved.", now, ct)
                .ConfigureAwait(false);
            return;
        }
        var currentRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var projectId = currentRun?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(currentRun.Pipeline, ct).ConfigureAwait(false);
        var hasPriorContract = projectId is not null
            && await IsMissingArtifactRequiredByPriorContractAsync(
                releaseSelector, projectId.Value, currentRun?.CommitHash,
                stepDefinition.Artifact, ct).ConfigureAwait(false);
        if (projectId is null || hasPriorContract)
        {
            await FailRestoreArtifactsAsync(runId, server, stepRun, error!, now, ct).ConfigureAwait(false);
            return;
        }
        await CompleteMissingRestoreArtifactAsync(
            runId, server, stepRun,
            "the selected release contract does not require this retained artifact; explicit fallback selected.", now, ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsMissingArtifactRequiredByPriorContractAsync(
        string selector,
        int projectId,
        string? commitHash,
        string? artifactName,
        CancellationToken ct)
    {
        if (string.Equals(selector, "previous-published", StringComparison.OrdinalIgnoreCase))
            return IsGitCommitHash(commitHash)
                && await artifactRepo.RequiresPreviousPublishedArtifactAsync(
                    projectId, commitHash!, artifactName, ct).ConfigureAwait(false);
        if (string.Equals(selector, "previous-deployed", StringComparison.OrdinalIgnoreCase))
            return IsGitCommitHash(commitHash)
                && await artifactRepo.RequiresPreviousDeployedArtifactAsync(
                    projectId, commitHash!, artifactName, ct).ConfigureAwait(false);
        return string.Equals(selector, "current-deployed", StringComparison.OrdinalIgnoreCase)
            ? await artifactRepo.HasDeployedRollbackContractReleaseAsync(projectId, ct).ConfigureAwait(false)
            : await artifactRepo.HasPublishedRollbackContractReleaseAsync(projectId, ct).ConfigureAwait(false);
    }

    private async Task FailRestoreArtifactsAsync(
        int runId,
        Server server,
        PipelineStepRun stepRun,
        string reason,
        DateTime now,
        CancellationToken ct)
    {
        stepRun.ServerId = server.Id;
        MarkSystemStepFailed(stepRun, TaskFailureCodes.ToolError, reason, now);
        await repo.AppendRunWarningsAsync(
            runId, [$"Restore-artifacts step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
    }

    private async Task CompleteMissingRestoreArtifactAsync(
        int runId,
        Server server,
        PipelineStepRun stepRun,
        string warning,
        DateTime now,
        CancellationToken ct)
    {
        stepRun.ServerId = server.Id;
        stepRun.Status = TaskExecutionStatus.Success;
        stepRun.StartedAt ??= now;
        stepRun.CompletedAt = now;
        await repo.AppendRunWarningsAsync(
            runId, [$"Restore-artifacts step '{stepRun.StepName}': {warning}"], ct).ConfigureAwait(false);
    }

    private void CreateRestoreArtifactsTask(
        int runId,
        Server server,
        PipelineStepRun stepRun,
        PipelineStepDefinition stepDefinition,
        Dictionary<string, string> legVariables,
        PipelineArtifact artifact,
        string artifactSha256,
        string releaseSelector,
        string targetDirectory)
    {
        var restoreVars = new Dictionary<string, string>(legVariables, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_RESTORE_ARTIFACT_ID"] = artifact.Id.ToString(),
            ["AETHEUS_RESTORE_RUN_ID"] = runId.ToString(),
            ["AETHEUS_RESTORE_ARTIFACT_SHA256"] = artifactSha256,
            ["AETHEUS_RESTORE_ARTIFACT_SIZE_BYTES"] = artifact.SizeBytes.ToString(),
            ["AETHEUS_WORKING_DIR"] = legVariables.GetValueOrDefault("WORKSPACE", ""),
            ["AETHEUS_RESTORE_RELEASE_SELECTOR"] = releaseSelector
        };
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            restoreVars["AETHEUS_RESTORE_TARGET_DIR"] = targetDirectory;

        stepRun.ServerId = server.Id;
        var restoreTask = new ServerTask
        {
            ServerId = server.Id,
            Name = stepRun.StepName,
            Command = artifact.Name,
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(restoreVars)),
            TimeoutSeconds = stepDefinition.TimeoutSeconds,
            Operation = OperationKind.PipelineRestoreArtifacts
        };
        repo.TrackTask(restoreTask);
        MarkStepDispatched(stepRun, restoreTask);
    }

    public async Task<(PipelineArtifact? Artifact, string? Error)> ResolveArtifactSourceAsync(
        int runId, Dictionary<string, string> variables, PipelineStepDefinition stepDef,
        CancellationToken ct)
    {
        var releaseSelector = SubstituteVariables(stepDef.Release ?? string.Empty, variables).Trim();
        var artifactName = SubstituteVariables(stepDef.Artifact ?? string.Empty, variables).Trim();
        var sourcePipelineName = SubstituteVariables(stepDef.ArtifactSourcePipeline ?? string.Empty, variables).Trim();
        if (!string.IsNullOrWhiteSpace(releaseSelector))
            return await ResolveReleaseArtifactAsync(
                runId, releaseSelector, artifactName, sourcePipelineName, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(artifactName) || string.IsNullOrWhiteSpace(sourcePipelineName))
            return (null, "'release' or both 'artifact' and 'artifact_source_pipeline' are required.");

        var context = await ResolveArtifactContextAsync(runId, ct).ConfigureAwait(false);
        if (context.Error is not null) return (null, context.Error);
        var lineageRootRunId = ResolveLineageRootRunId(runId, variables);
        var sourceRunId = await repo.FindTriggeredRunIdByPipelineNameAsync(
            lineageRootRunId, sourcePipelineName, ct).ConfigureAwait(false);
        if (sourceRunId is { } verifiedRunId)
            return await ResolveTriggeredArtifactAsync(
                verifiedRunId, context.Context!, artifactName, sourcePipelineName, ct).ConfigureAwait(false);
        var existingArtifact = await artifactRepo.FindSuccessfulPipelineArtifactByCommitAsync(
            context.Context!.ProjectId, sourcePipelineName, context.Context.Run.CommitHash!, artifactName, ct).ConfigureAwait(false);
        return existingArtifact is null
            ? (null, $"artifact '{artifactName}' was not found on a successful {sourcePipelineName} run at commit {context.Context.Run.CommitHash}.")
            : (existingArtifact, null);
    }

    private async Task<(ArtifactResolutionContext? Context, string? Error)> ResolveArtifactContextAsync(
        int runId, CancellationToken ct)
    {
        var currentRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (currentRun?.Pipeline is null || !IsGitCommitHash(currentRun.CommitHash))
            return (null, "the current run has no verified source commit.");
        var projectId = await repo.GetPipelineProjectIdAsync(currentRun.Pipeline, ct).ConfigureAwait(false);
        return projectId is null
            ? (null, "the current run is not attached to a project.")
            : (new ArtifactResolutionContext(currentRun, projectId.Value), null);
    }

    private static int ResolveLineageRootRunId(int runId, IReadOnlyDictionary<string, string> variables) =>
        variables.TryGetValue("UPSTREAM_RUN_ID", out var parentRunIdText)
        && int.TryParse(parentRunIdText, out var parentRunId)
        && parentRunId > 0
            ? parentRunId
            : runId;

    private async Task<(PipelineArtifact? Artifact, string? Error)> ResolveTriggeredArtifactAsync(
        int sourceRunId,
        ArtifactResolutionContext context,
        string artifactName,
        string sourcePipelineName,
        CancellationToken ct)
    {
        var sourceRun = await repo.GetPipelineRunWithPipelineAsync(sourceRunId, ct).ConfigureAwait(false);
        var sourceProjectId = sourceRun?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(sourceRun.Pipeline, ct).ConfigureAwait(false);
        if (sourceProjectId != context.ProjectId)
            return (null, "the source run belongs to another project.");
        if (!string.Equals(sourceRun?.CommitHash, context.Run.CommitHash, StringComparison.OrdinalIgnoreCase))
            return (null, "the source run belongs to another source commit.");
        if (sourceRun?.Status != PipelineStatus.Success)
            return (null, "the source run did not succeed; its optional producer artifact is unavailable.");
        var artifact = await artifactRepo.FindRunArtifactByNameAsync(
            sourceRunId, artifactName, ct).ConfigureAwait(false);
        return artifact?.ProjectId == context.ProjectId
            ? (artifact, null)
            : (null, $"artifact '{artifactName}' was not found on successful {sourcePipelineName} run {sourceRunId}.");
    }

    private sealed record ArtifactResolutionContext(PipelineRun Run, int ProjectId);

    private async Task<(PipelineArtifact? Artifact, string? Error)> ResolveReleaseArtifactAsync(
        int runId,
        string releaseSelector,
        string artifactName,
        string sourcePipelineName,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sourcePipelineName))
            return (null, "'release' cannot be combined with 'artifact_source_pipeline'.");
        var releaseRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var projectId = releaseRun?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(releaseRun.Pipeline, ct).ConfigureAwait(false);
        if (projectId is null)
            return (null, "the current run is not attached to a project.");

        var artifactFilter = string.IsNullOrWhiteSpace(artifactName) ? null : artifactName;
        PipelineArtifact? artifact;
        var previousPublished = string.Equals(
            releaseSelector, "previous-published", StringComparison.OrdinalIgnoreCase);
        var previousDeployed = string.Equals(
            releaseSelector, "previous-deployed", StringComparison.OrdinalIgnoreCase);
        if (previousPublished || previousDeployed)
        {
            if (!IsGitCommitHash(releaseRun?.CommitHash))
                return (null, "the current run has no verified source commit.");
            artifact = previousDeployed
                ? await artifactRepo.FindPreviousDeployedReleaseArtifactAsync(
                    projectId.Value, releaseRun!.CommitHash!, artifactFilter, ct).ConfigureAwait(false)
                : await artifactRepo.FindPreviousPublishedReleaseArtifactAsync(
                    projectId.Value, releaseRun!.CommitHash!, artifactFilter, ct).ConfigureAwait(false);
        }
        else
        {
            artifact = await artifactRepo.FindReleaseArtifactAsync(
                projectId.Value, releaseSelector, artifactFilter, ct).ConfigureAwait(false);
        }
        return artifact is null
            ? (null, $"release '{releaseSelector}' has no retained artifact in this project.")
            : (artifact, null);
    }

    private static bool IsBootstrapReleaseSelector(string? selector) =>
        string.Equals(selector, "latest-published", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "previous-published", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "previous-deployed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "current-deployed", StringComparison.OrdinalIgnoreCase);

    private static bool IsOptionalProducerArtifactUnavailable(
        PipelineStepDefinition step,
        string? error) =>
        !string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline)
        && (error?.StartsWith("artifact '", StringComparison.Ordinal) == true
            || error?.Contains("optional producer artifact is unavailable", StringComparison.Ordinal) == true);

    public async Task CreateBackupRestoreTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        async Task FailAsync(string reason)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            stepRun.ServerId = legServer.Id;
            MarkSystemStepFailed(stepRun, TaskFailureCodes.ToolError, reason, now);
            await repo.AppendRunWarningsAsync(runId, [$"Restore-backup step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var selector = PipelineCommandBuilder.Substitute(stepDef.BackupRun ?? string.Empty, legVars);
        if (!int.TryParse(selector, out var backupRunId))
        {
            await FailAsync("backup_run must resolve to a verified backup run id.").ConfigureAwait(false);
            return;
        }

        var backup = backupRepo is null ? null
            : await backupRepo.FindRunWithPolicyAsync(backupRunId, ct).ConfigureAwait(false);
        if (!IsUsableBackup(backup, legServer.Id))
        {
            await FailAsync("the selected backup is not successful, verified, or owned by this target server.").ConfigureAwait(false);
            return;
        }
        var verifiedBackup = backup!;

        // A verified archive is still not interchangeable between projects.  Resolve the project
        // from the run being executed (rather than trusting the YAML or a caller-provided id) before
        // exposing the archive and DB credentials to the agent.
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var runProjectId = run?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (runProjectId is null || runProjectId != verifiedBackup.BackupPolicy!.ProjectId)
        {
            await FailAsync("the selected backup belongs to a different project.").ConfigureAwait(false);
            return;
        }

        var restoreVars = BuildBackupRestoreVariables(verifiedBackup, legVars);

        stepRun.ServerId = legServer.Id;
        var restoreTask = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = verifiedBackup.Id.ToString(),
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(restoreVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.BackupRestore
        };
        repo.TrackTask(restoreTask);
        MarkStepDispatched(stepRun, restoreTask);
    }

    private static bool IsUsableBackup(BackupRun? backup, int serverId) =>
        backup?.BackupPolicy is not null
        && backup.ServerId == serverId
        && backup.Status == BackupRunStatus.Succeeded
        && backup.RestoreCheckStatus == RestoreCheckStatus.Verified
        && !string.IsNullOrWhiteSpace(backup.ArchivePath);

    private Dictionary<string, string> BuildBackupRestoreVariables(
        BackupRun backup, IReadOnlyDictionary<string, string> legVars)
    {
        var policy = backup.BackupPolicy!;
        var variables = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            [BackupConstants.EngineEnvVar] = policy.DbEngine.ToString(),
            [BackupConstants.PolicyIdEnvVar] = policy.Id.ToString(),
            [BackupConstants.RunIdEnvVar] = backup.Id.ToString(),
            [BackupConstants.ArchivePathEnvVar] = backup.ArchivePath!
        };
        if (!string.IsNullOrWhiteSpace(policy.DbHost)) variables[BackupConstants.DbHostEnvVar] = policy.DbHost;
        if (policy.DbPort is not null) variables[BackupConstants.DbPortEnvVar] = policy.DbPort.Value.ToString();
        if (!string.IsNullOrWhiteSpace(policy.DbName)) variables[BackupConstants.DbNameEnvVar] = policy.DbName;
        if (!string.IsNullOrWhiteSpace(policy.DbUser)) variables[BackupConstants.DbUserEnvVar] = policy.DbUser;
        if (!string.IsNullOrWhiteSpace(policy.DbPasswordEncrypted))
            variables[BackupConstants.DbPasswordEnvVar] = encryption.DecryptValue(policy.DbPasswordEncrypted);
        return variables;
    }

}
