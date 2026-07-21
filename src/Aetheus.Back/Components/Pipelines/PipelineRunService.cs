// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Helpers;
using Aetheus.Shared.Validation;
using Microsoft.AspNetCore.SignalR;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

public class PipelineRunService(
    IPipelineRepository repo,
    IPipelineGitService pipelineGit,
    IHubContext<PipelineHub> pipelineHub,
    IPipelineVariableResolver variableResolver,
    IDomainEventDispatcher domainEvents,
    ISecretMaskingService secretMasking,
    IAuditService audit,
    IResourceAuthorizationService authz,
    IHttpClientFactory httpClientFactory,
    ILogger<PipelineRunService> logger,
    TimeProvider timeProvider,
    IGitGraphRecorder gitGraph,
    // Deliberate reach-in to the Artifacts repository (not IArtifactService): ArtifactService already
    // depends on IPipelineRunService (IsServerAssignedToRunAsync), so injecting IArtifactService here
    // would form a DI cycle. The two artifact-id lookups below are read-only resolution; this is the
    // documented cycle-breaker rather than a layering violation. See audit Pipeline-Architecture.
    IArtifactRepository artifactRepo,
    IEncryptionService encryption,
    // PLAN-001 phase 2 zero-config: injects OTEL env into a deployed app when a MonitoredApp is linked.
    // No-op (empty map) unless AppMonitoring:IngestBaseUrl is set and a monitored app exists, so deploys
    // that don't opt in are unaffected.
    Aetheus.Back.Components.AppMonitoring.IAppDeployEnvProvider appDeployEnv,
    IConfiguration configuration,
    IPipelineTemplateResolver templateResolver,
    IBackupRepository? backupRepo = null) : IPipelineRunService, IPipelineLauncher
{
    internal const string SystemPrepareStage = "System:Prepare";
    internal const string SystemCleanupStage = "System:Cleanup";
    private const string SourceCommitVariable = "AETHEUS_SOURCE_COMMIT";
    private const string SourceBranchVariable = "AETHEUS_RUN_BRANCH";

    // Per-run lock: serializes AdvanceStageAsync calls so concurrent task completions
    // within the same stage don't race on status updates. Refcounted (see RunAdvanceLock):
    // entries self-evict when the last holder/waiter releases, so no manual TryRemove.
    private static readonly RunAdvanceLock _runLocks = new();
    private IPipelineTemplateResolver TemplateResolver => templateResolver;

    public async Task<PipelineRunDto?> TriggerRunAsync(int id, Dictionary<string, string>? additionalVariables = null,
        Dictionary<string, string>? parameters = null, CancellationToken ct = default)
        => await TriggerResolvedRunAsync(id, additionalVariables, parameters,
            yamlOverride: null, ResolveSourceCommit(additionalVariables), ct).ConfigureAwait(false);

    public Task<PipelineRunPreparation?> PrepareRunAsync(int pipelineId, string? sourceBranch = null,
        CancellationToken ct = default)
        => PrepareRunCoreAsync(pipelineId, sourceBranch, yamlOverride: null, commitOverride: null, ct);

    private async Task<PipelineRunDto?> TriggerResolvedRunAsync(int id,
        Dictionary<string, string>? additionalVariables, Dictionary<string, string>? parameters,
        string? yamlOverride, string? commitOverride, CancellationToken ct)
    {
        var preparation = await PrepareRunCoreAsync(
            id, ResolveRunBranch(additionalVariables), yamlOverride, commitOverride, ct).ConfigureAwait(false);
        return preparation is null
            ? null
            : await TriggerPreparedRunAsync(preparation, additionalVariables, parameters, ct).ConfigureAwait(false);
    }

    public async Task<List<PipelineRunParameterDto>> GetRunParametersAsync(int pipelineId, string? sourceBranch = null, CancellationToken ct = default)
    {
        var pipeline = await repo.FindPipelineAsync(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null) return [];

        // Read the authoritative (git-first) definition the same way the trigger does, so the dialog
        // reflects what will actually run for a project-owned pipeline.
        var yaml = pipeline.YamlDefinition;
        if (pipeline.ProjectId is { } projectId)
        {
            var gitYaml = await pipelineGit.ReadProjectPipelineYamlAsync(
                projectId, pipeline.Name, ct, sourceBranch ?? pipeline.SourceBranch).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(gitYaml)) yaml = gitYaml;
        }

        var organizationId = await repo.GetPipelineOrganizationIdAsync(pipelineId, ct).ConfigureAwait(false) ?? 0;
        var resolution = await TemplateResolver.ResolveAsync(yaml, organizationId, parameters: null, ct)
            .ConfigureAwait(false);
        var definition = resolution.Definition;

        return definition.Parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new PipelineRunParameterDto
            {
                Name = p.Name,
                DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName!,
                Type = p.Type,
                Default = p.Default,
                Required = p.Required,
                Description = p.Description,
                AllowedValues = p.AllowedValues
            })
            .ToList();
    }

    private async Task<PipelineRunPreparation?> PrepareRunCoreAsync(int id, string? sourceBranch,
        string? yamlOverride, string? commitOverride, CancellationToken ct)
    {
        var pipeline = await repo.FindPipelineAsync(id, ct).ConfigureAwait(false);
        if (pipeline is null) return null;

        var mirroredDefinition = YamlParsingHelper.ParseAndValidate(pipeline.YamlDefinition, logger);
        var branch = sourceBranch
            ?? mirroredDefinition?.SourceBranch
            ?? pipeline.SourceBranch
            ?? pipeline.Project?.DefaultBranch;
        if (!string.IsNullOrWhiteSpace(branch) && !PipelineBranchValidator.IsValid(branch))
            throw new BadRequestException("The selected source branch is invalid.");

        var effectiveProjectId = pipeline.ProjectId
            ?? await repo.GetPipelineProjectIdAsync(pipeline, ct).ConfigureAwait(false);
        var commitHash = commitOverride;
        if (commitHash is null && effectiveProjectId is { } commitProjectId)
            commitHash = await pipelineGit.GetHeadCommitShaAsync(commitProjectId, ct, branch).ConfigureAwait(false);

        var definitionYaml = yamlOverride ?? pipeline.YamlDefinition;
        if (yamlOverride is null && effectiveProjectId is { } gitProjectId)
        {
            var gitYaml = !string.IsNullOrWhiteSpace(commitHash)
                ? await pipelineGit.ReadProjectPipelineYamlAtRevisionAsync(
                    gitProjectId, pipeline.Name, commitHash, ct).ConfigureAwait(false)
                : await pipelineGit.ReadProjectPipelineYamlAsync(
                    gitProjectId, pipeline.Name, ct, branch).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(gitYaml)) definitionYaml = gitYaml;
        }

        var organizationId = await repo.GetPipelineOrganizationIdAsync(id, ct).ConfigureAwait(false);
        var resolution = await TemplateResolver.ResolveAsync(definitionYaml, organizationId ?? 0, parameters: null, ct)
            .ConfigureAwait(false);
        definitionYaml = resolution.Yaml;
        var definition = resolution.Definition;
        var effectiveStages = YamlParsingHelper.FlattenJobs(definition);
        var matrixErrors = ValidateMatrixValues(effectiveStages);
        if (matrixErrors.Count > 0)
            throw new BadRequestException(string.Join(" ", matrixErrors));
        var isolationErrors = ValidateIsolationLimits(effectiveStages);
        if (isolationErrors.Count > 0)
            throw new BadRequestException(string.Join(" ", isolationErrors));
        var executionRoleErrors = ValidateExecutionRoles(effectiveStages);
        if (executionRoleErrors.Count > 0)
            throw new BadRequestException(string.Join(" ", executionRoleErrors));

        var targetIds = await ResolveCandidateTargetServerIdsAsync(definition, organizationId, ct).ConfigureAwait(false);
        return new PipelineRunPreparation
        {
            PipelineId = id,
            Pipeline = pipeline,
            BranchName = branch,
            CommitHash = commitHash,
            YamlSnapshot = definitionYaml,
            Definition = definition,
            EffectiveStages = effectiveStages,
            EffectiveProjectId = effectiveProjectId,
            TargetServerIds = targetIds
        };
    }

    public async Task<PipelineRunDto?> TriggerPreparedRunAsync(PipelineRunPreparation preparation,
        Dictionary<string, string>? additionalVariables = null,
        Dictionary<string, string>? parameters = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var definition = preparation.Definition;
        var variables = additionalVariables is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(additionalVariables, StringComparer.OrdinalIgnoreCase);

        var reservedParameter = parameters?.Keys.FirstOrDefault(PipelineParameterResolver.IsReservedName);
        if (reservedParameter is not null)
            throw new BadRequestException($"Parameter '{reservedParameter}' uses a reserved system variable name.");

        if (!PipelineParameterResolver.TryResolve(definition.Parameters, parameters, out var effectiveParams, out var paramErrors))
            throw new BadRequestException(string.Join(" ", paramErrors));
        if (parameters is { Count: > 0 })
        {
            foreach (var (k, v) in parameters)
                variables[k] = v;
        }
        if (effectiveParams.Count > 0)
        {
            foreach (var (k, v) in effectiveParams)
                variables[PipelineCommandBuilder.ParameterKeyPrefix + k] = v;
        }

        if (!string.IsNullOrWhiteSpace(preparation.BranchName))
            variables[SourceBranchVariable] = preparation.BranchName;
        if (IsGitCommitHash(preparation.CommitHash))
            variables[SourceCommitVariable] = preparation.CommitHash!;

        var runtimePreparation = await ResolveRunParametersAsync(
            preparation, effectiveParams, ct).ConfigureAwait(false);

        var run = new PipelineRun
        {
            PipelineId = preparation.PipelineId,
            Status = PipelineStatus.Running,
            StartedAt = timeProvider.GetUtcNow().UtcDateTime,
            YamlSnapshot = runtimePreparation.YamlSnapshot,
            BranchName = preparation.BranchName,
            CommitHash = preparation.CommitHash,
            AdditionalVariablesJson = variables.Count > 0 ? JsonSerializer.Serialize(variables) : "{}",
            ParametersJson = effectiveParams.Count > 0 ? JsonSerializer.Serialize(effectiveParams) : "{}"
        };

        await repo.AddPipelineRunAsync(run, ct).ConfigureAwait(false);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            return await LaunchRunAsync(run, runtimePreparation, variables, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a launch failure - let it propagate
        }
        catch (Exception ex)
        {
            // Product requirement: a failed launch must remain VISIBLE in the run list with its reason,
            // rather than a bare 500 with no row. Convert any setup failure (git read, invalid committed
            // YAML, variable resolution, task dispatch) into a failed run carrying the message. This is one
            // of the sanctioned try/catch cases - ErrorHandlingMiddleware cannot attach the failure to a
            // run it never sees.
            logger.LogError(ex, "Pipeline run {RunId} failed during launch setup", run.Id);
            await repo.AppendRunWarningsAsync(run.Id, [BuildLaunchFailureReason(ex)], ct).ConfigureAwait(false);
            await repo.UpdatePipelineRunStatusAsync(run.Id, PipelineStatus.Failed, ct).ConfigureAwait(false);
            await pipelineHub.Clients
                .Groups(HubGroups.PipelineRunUpdates(run.Id, preparation.PipelineId))
                .SendAsync("PipelineRunCompleted", run.Id, PipelineStatus.Failed, ct).ConfigureAwait(false);
            return await GetRunAsync(run.Id, ct).ConfigureAwait(false);
        }
    }

    public async Task<PipelineRunPreparation> ResolveRunParametersAsync(
        PipelineRunPreparation preparation,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (!PipelineParameterResolver.TryResolve(
                preparation.Definition.Parameters, parameters, out var effectiveParameters, out var errors))
            throw new BadRequestException(string.Join(" ", errors));

        var organizationId = await repo.GetPipelineOrganizationIdAsync(preparation.PipelineId, ct)
            .ConfigureAwait(false) ?? 0;
        var resolution = await TemplateResolver.ResolveAsync(
            preparation.YamlSnapshot, organizationId, effectiveParameters, ct).ConfigureAwait(false);
        var effectiveStages = YamlParsingHelper.FlattenJobs(resolution.Definition);
        var targetIds = await ResolveCandidateTargetServerIdsAsync(
            resolution.Definition, organizationId, ct).ConfigureAwait(false);
        return preparation with
        {
            YamlSnapshot = resolution.Yaml,
            Definition = resolution.Definition,
            EffectiveStages = effectiveStages,
            TargetServerIds = targetIds
        };
    }

    // The user-facing reason persisted on a run that failed during launch setup (git read, invalid
    // committed YAML, variable resolution, dispatch). Secret values are masked upstream, so an exception
    // message here carries names/paths, not secrets.
    private static string BuildLaunchFailureReason(Exception ex)
        => $"The run could not be launched: {ex.Message}";

    // Post-persist launch setup. Git is deliberately not read here: authorization and execution must use
    // the same immutable preparation resolved before the run was created.
    private async Task<PipelineRunDto?> LaunchRunAsync(
        PipelineRun run, PipelineRunPreparation preparation,
        Dictionary<string, string> additionalVariables, CancellationToken ct)
    {
        var pipeline = preparation.Pipeline;
        var definition = preparation.Definition;
        var effectiveStages = preparation.EffectiveStages;
        var effectiveProjectId = preparation.EffectiveProjectId;

        // Inject project-level system vars that require a DB lookup.
        var projectVars = new Dictionary<string, string>(additionalVariables, StringComparer.OrdinalIgnoreCase);
        // S-TECH-RPVI: single injection point shared with PipelineVariableResolver so the trigger-time
        // snapshot and the per-run re-resolution can never diverge (which once froze the localhost mirror
        // authority on the first-stage clone). The mirror URL is rehomed inside Inject.
        PipelineProjectVariables.Inject(
            projectVars, pipeline.Project, configuration, run.BranchName ?? pipeline.SourceBranch);
        if (!string.IsNullOrWhiteSpace(run.CommitHash))
            projectVars["BUILD_SOURCEVERSION"] = run.CommitHash;

        var (resolvedVars, warnings, secretKeys) = await ResolveVariablesWithWarningsAsync(
            definition, effectiveProjectId, projectVars, ct,
            pipelineId: run.PipelineId, runId: run.Id, pipelineName: pipeline.Name).ConfigureAwait(false);
        var publicVars = FilterSecretKeys(resolvedVars, secretKeys);
        run.ResolvedVariablesJson = JsonSerializer.Serialize(publicVars);
        run.WarningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null;

        // System:Prepare (clone/prepare workspace) and System:Cleanup only make sense when at least one
        // step actually uses the workspace. A pure orchestration pipeline - every step is `type: trigger`,
        // which merely launches and waits on a child run - needs neither, so skip both. Keeps the run view
        // clean (no useless "Clone Repository"/"Cleanup") and lets the trigger steps stand on their own.
        var requiresWorkspace = RequiresWorkspace(definition);
        var hasRepo = !string.IsNullOrEmpty(pipeline.Project?.RepositoryUrl);
        var order = 0;
        if (requiresWorkspace)
        {
            repo.TrackPipelineStepRun(new PipelineStepRun
            {
                PipelineRunId = run.Id,
                StageName = SystemPrepareStage,
                StepName = hasRepo ? "Clone Repository" : "Prepare Workspace",
                Order = order++,
                IsSystem = true
            });
        }

        // Create step runs for each stage/step (with matrix expansion) - effectiveStages was
        // flattened once above (shared with the matrix validation).
        foreach (var stage in effectiveStages)
        {
            var matrixLegs = ExpandMatrix(stage.Matrix);

            foreach (var leg in matrixLegs)
            {
                var legName = leg.Count > 0 ? string.Join("-", leg.Values) : null;

                foreach (var step in stage.Steps)
                {
                    repo.TrackPipelineStepRun(new PipelineStepRun
                    {
                        PipelineRunId = run.Id,
                        StageName = stage.Name,
                        StepName = legName is not null ? $"{step.Name} [{legName}]" : step.Name,
                        Order = order++,
                        RetryCount = step.RetryCount,
                        ContinueOnError = step.ContinueOnError,
                        MatrixLeg = legName,
                        GroupName = stage.Group
                    });
                }
            }

            // Artifact collection is dispatched post-stage (in AdvanceStageAsync),
            // not as an injected step - avoids the task claim concurrency bug.
        }

        // Inject system cleanup step (always runs) - only when the workspace was prepared above.
        if (requiresWorkspace)
        {
            repo.TrackPipelineStepRun(new PipelineStepRun
            {
                PipelineRunId = run.Id,
                StageName = SystemCleanupStage,
                StepName = "Cleanup",
                Order = order++,
                IsSystem = true,
                ContinueOnError = true
            });
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await pipelineHub.Clients.Groups(HubGroups.PipelineRunUpdates(run.Id, run.PipelineId)).SendAsync("PipelineRunStarted", run.Id, run.PipelineId, ct).ConfigureAwait(false);

        await CreateTasksForNextStageAsync(run.Id, definition, resolvedVars, secretKeys, ct).ConfigureAwait(false);

        await audit.LogAsync("Triggered", "PipelineRun", run.Id, pipeline.Name, ct).ConfigureAwait(false);

        // Record git provenance so the run-detail view can resolve internal commit/branch pages and
        // the cross-link graph stays current beyond the one-shot migration backfill (best-effort).
        if (effectiveProjectId is { } recProjectId)
            await gitGraph.RecordRunContextAsync(recProjectId, run.CommitHash, run.BranchName, ct).ConfigureAwait(false);

        return await GetRunAsync(run.Id, ct).ConfigureAwait(false);
    }

    public async Task<PipelineRunDto?> TriggerAutomatedRunAsync(
        int pipelineId, string triggerSource, Dictionary<string, string>? additionalVariables = null, CancellationToken ct = default)
    {
        var pipeline = await repo.FindPipelineAsync(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null) return null;

        // F-EXEC-1b: a pipeline step is free-form shell (= RCE). Interactive runs authorize the
        // live caller (F-EXEC-1); webhook/scheduler have no principal, so authorize the owner.
        var owner = pipeline.CreatedByUsername;
        if (string.IsNullOrEmpty(owner))
        {
            logger.LogWarning(
                "{Trigger} run of pipeline {PipelineId} blocked (F-EXEC-1b): pipeline has no owner. " +
                "Re-save it as an authorized user to enable automated triggers.", triggerSource, pipelineId);
            await audit.LogAsync("BlockedUnownedAutomatedRun", "Pipeline", pipelineId, pipeline.Name, ct).ConfigureAwait(false);
            return null;
        }

        var preparation = await PrepareRunCoreAsync(pipelineId, ResolveRunBranch(additionalVariables),
            yamlOverride: null, ResolveSourceCommit(additionalVariables), ct).ConfigureAwait(false);
        if (preparation is null) return null;
        var targetIds = preparation.TargetServerIds;
        foreach (var serverId in targetIds)
        {
            if (!await authz.HasPermissionAsync(owner, ResourceType.Server, serverId, Permission.Admin, ct).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "{Trigger} run of pipeline {PipelineId} blocked (F-EXEC-1b): owner '{Owner}' lacks " +
                    "Server.Admin on target server {ServerId}.", triggerSource, pipelineId, owner, serverId);
                await audit.LogAsync("BlockedUnauthorizedAutomatedRun", "Pipeline", pipelineId,
                    $"{pipeline.Name} | Servers: {string.Join(',', targetIds)}", ct).ConfigureAwait(false);
                return null;
            }
        }

        // F-026: log the resolved server list so automated-run audits are traceable.
        await audit.LogAsync("AutomatedRunAuthorized", "Pipeline", pipelineId,
            $"{pipeline.Name} | Servers: {string.Join(',', targetIds)}", ct).ConfigureAwait(false);

        return await TriggerPreparedRunAsync(preparation, additionalVariables, ct: ct).ConfigureAwait(false);
    }

    public async Task<PipelineRunDto?> RerunAsync(int sourceRunId, RerunMode mode, CancellationToken ct = default)
    {
        var source = await repo.GetRunDetailAsync(sourceRunId, ct).ConfigureAwait(false);
        if (source is null) return null;

        // Reproduce the original queue-time inputs (user-supplied additional variables).
        var vars = DeserializeResolvedVariables(source.AdditionalVariablesJson);
        var additional = vars.Count > 0 ? vars : null;

        // P: replay the original run's queue-time parameters so the rerun's snapshot/precedence match
        // (the user values are also carried in `additional`; re-passing them is idempotent).
        var sourceParams = DeserializeResolvedVariables(source.ParametersJson);
        var rerunParams = sourceParams.Count > 0 ? sourceParams : null;

        // Snapshot modes need the captured YAML; a legacy run without one falls back to a fresh run
        // from the live definition (honest degradation - never a silent no-op).
        return mode switch
        {
            RerunMode.SnapshotSameCommit when !string.IsNullOrWhiteSpace(source.YamlSnapshot) =>
                await TriggerResolvedRunAsync(source.PipelineId, additional, rerunParams, source.YamlSnapshot, source.CommitHash, ct).ConfigureAwait(false),
            RerunMode.SnapshotBranchHead when !string.IsNullOrWhiteSpace(source.YamlSnapshot) =>
                await TriggerResolvedRunAsync(source.PipelineId, additional, rerunParams, source.YamlSnapshot, commitOverride: null, ct).ConfigureAwait(false),
            _ => await TriggerResolvedRunAsync(source.PipelineId, additional, rerunParams, yamlOverride: null, commitOverride: null, ct).ConfigureAwait(false)
        };
    }

    public async Task<PaginatedResult<PipelineRunDto>> GetRunsAsync(int pipelineId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        // Already projected server-side (slim list DTOs - no commands/output vars/yaml).
        var (runs, total) = await repo.GetRunsPagedAsync(pipelineId, page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<PipelineRunDto>
        {
            Items = runs,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<List<PipelineRunDto>> GetActiveRunsAsync(
        List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default) =>
        repo.GetActiveRunsAsync(accessiblePipelineIds, projectId, ct);

    public Task<List<PipelineRunDto>> GetRecentRunsAsync(
        List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default) =>
        repo.GetRecentRunsAsync(accessiblePipelineIds, projectId, ct);

    public async Task<PipelineRunDto?> GetRunAsync(int runId, CancellationToken ct = default)
    {
        var run = await repo.GetRunDetailAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return null;

        var dto = MapRunToDto(run);
        // Resolve the run's commit/branch to internal git-graph pages (mirrors the Release/Artifact
        // detail views). Cheap point lookups; empty when no graph node exists yet (plain-text fallback).
        if (run.Pipeline?.ProjectId is { } projectId)
        {
            var (commits, branches) = await gitGraph.ResolveRunLinksAsync(projectId, run.CommitHash, run.BranchName, ct).ConfigureAwait(false);
            dto = dto with { Commits = commits, Branches = branches };
        }

        // S-UX-18: surface each step's executed command, but mask substituted secret values first -
        // ServerTask.Command stores the resolved command verbatim (unlike logs, masked at write time).
        // OutputVariables get the same treatment: a step can `setvariable` a secret's value, which
        // would otherwise reach any Project.Read user verbatim, bypassing the log masking.
        if (dto.Steps.Any(s => !string.IsNullOrEmpty(s.Command) || s.OutputVariables.Count > 0))
        {
            var maskedSteps = new List<PipelineStepRunDto>(dto.Steps.Count);
            foreach (var step in dto.Steps)
            {
                var masked = step;
                if (!string.IsNullOrEmpty(step.Command))
                    masked = masked with { Command = await secretMasking.MaskAsync(step.Command, runId, ct).ConfigureAwait(false) };
                if (step.OutputVariables.Count > 0)
                {
                    var maskedVars = new Dictionary<string, string>(step.OutputVariables.Count);
                    foreach (var (key, value) in step.OutputVariables)
                        maskedVars[key] = await secretMasking.MaskAsync(value, runId, ct).ConfigureAwait(false);
                    masked = masked with { OutputVariables = maskedVars };
                }
                maskedSteps.Add(masked);
            }
            dto = dto with { Steps = maskedSteps };
        }

        // Trigger steps are NOT flattened into this run's step list. Each keeps its TriggeredRunId +
        // TriggeredPipelineName so the run view renders the child pipeline as a collapsible node and
        // lazy-loads its own run detail on expand (recursive tree, keyed by run id - see the front
        // PipelineRun timeline). Flattening here produced an unreadable, duplicate-prone linear list.
        return dto;
    }

    public async Task<DryRunResultDto?> DryRunAsync(int pipelineId, Dictionary<string, string>? additionalVars = null, CancellationToken ct = default)
    {
        var pipeline = await repo.FindPipelineAsync(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null) return null;

        var definition = YamlParsingHelper.ParseAndValidate(pipeline.YamlDefinition, logger);
        if (definition is null) return null;

        // S-TECH-D4XR: validate + preview any declared run parameters supplied among the additional
        // vars (same resolver the real trigger uses) - surfaced as warnings so the preview never throws.
        var paramWarnings = ValidateAndApplyParameters(definition, ref additionalVars);

        var dryRunProjectId = pipeline.ProjectId
            ?? await repo.GetPipelineProjectIdAsync(pipeline, ct).ConfigureAwait(false);
        var (resolvedVars, warnings, secretKeys) = await ResolveVariablesWithWarningsAsync(definition, dryRunProjectId, additionalVars, ct).ConfigureAwait(false);
        warnings.InsertRange(0, paramWarnings);

        var stages = new List<DryRunStageDto>();
        foreach (var stage in YamlParsingHelper.FlattenJobs(definition))
        {
            // Merge stage-level variables on top
            var stageVars = new Dictionary<string, string>(resolvedVars, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in stage.Variables)
                stageVars[key] = value;

            // Mask secret values in resolved commands so dry-run output is safe to display.
            var maskedVars = MaskSecretValues(stageVars, secretKeys);

            var steps = stage.Steps.Select(step => new DryRunStepDto
            {
                StepName = step.Name,
                OriginalCommand = step.Shell,
                ResolvedCommand = SubstituteVariables(step.Shell, maskedVars)
            }).ToList();

            stages.Add(new DryRunStageDto
            {
                StageName = stage.Name,
                Agent = stage.Agent,
                Os = stage.Os,
                Steps = steps
            });
        }

        return new DryRunResultDto
        {
            Stages = stages,
            ResolvedVariables = FilterSecretKeys(resolvedVars, secretKeys),
            Warnings = warnings
        };
    }

    public async Task<PipelinePreflightDto?> PreflightAsync(
        int pipelineId, Dictionary<string, string>? additionalVars = null, CancellationToken ct = default)
    {
        var preparation = await PrepareRunCoreAsync(pipelineId, ResolveRunBranch(additionalVars),
            yamlOverride: null, ResolveSourceCommit(additionalVars), ct).ConfigureAwait(false);
        return preparation is null
            ? null
            : await PreflightAsync(preparation, additionalVars, ct).ConfigureAwait(false);
    }

    public async Task<PipelinePreflightDto?> PreflightAsync(
        PipelineRunPreparation preparation, Dictionary<string, string>? additionalVars = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var pipeline = preparation.Pipeline;
        var definition = preparation.Definition;

        // S-TECH-D4XR: validate the supplied run parameters during preflight too, so a bad value is
        // reported alongside the unresolved-target warnings before the run is launched.
        var paramWarnings = ValidateAndApplyParameters(definition, ref additionalVars);

        var (_, warnings, _) = await ResolveVariablesWithWarningsAsync(
            definition, preparation.EffectiveProjectId, additionalVars, ct).ConfigureAwait(false);
        warnings.InsertRange(0, paramWarnings);

        var organizationId = await repo.GetPipelineOrganizationIdAsync(pipeline.Id, ct).ConfigureAwait(false);
        var stages = new List<PreflightStageDto>();
        foreach (var stage in YamlParsingHelper.FlattenJobs(definition))
        {
            var server = StageHasDeployStep(stage)
                ? await repo.FindOnlineDeployTargetAsync(stage.Pool, stage.Environment, stage.Agent,
                    OsTypeHelper.Parse(stage.Os), organizationId, ct).ConfigureAwait(false)
                : await ResolveServerAsync(stage, organizationId, ct).ConfigureAwait(false);
            var (kind, label) = DescribeTarget(stage);
            stages.Add(new PreflightStageDto
            {
                StageName = stage.Name,
                TargetKind = kind,
                Target = label,
                Resolved = server is not null,
                ServerName = server?.Name,
                Reason = server is null ? BuildNoServerReason(stage.Name, stage) : null
            });
        }

        return new PipelinePreflightDto { Stages = stages, Warnings = warnings };
    }

    // S-TECH-D4XR: pull the declared-parameter values out of the supplied additional vars, validate
    // them with the same resolver the real trigger uses, and merge the effective (supplied ?? default)
    // values back at highest precedence so the preview reflects them. Returns human-readable warnings
    // for any validation failure (preview is non-destructive, so we warn instead of throwing).
    private static List<string> ValidateAndApplyParameters(PipelineYamlDefinition definition, ref Dictionary<string, string>? additionalVars)
    {
        if (definition.Parameters.Count == 0) return [];

        var supplied = additionalVars?
            .Where(kv => definition.Parameters.Any(p => p.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        PipelineParameterResolver.TryResolve(definition.Parameters, supplied, out var effective, out var errors);
        if (effective.Count > 0)
        {
            additionalVars ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in effective)
            {
                additionalVars[k] = v;
                // S-TECH-V6QN: also preview the ${{ parameters.X }} namespace.
                additionalVars[PipelineCommandBuilder.ParameterKeyPrefix + k] = v;
            }
        }
        return errors.Select(e => $"Parameter: {e}").ToList();
    }

    public async Task<IReadOnlyCollection<int>> ResolveCandidateTargetServerIdsAsync(
        int pipelineId, CancellationToken ct = default)
    {
        var preparation = await PrepareRunAsync(pipelineId, ct: ct).ConfigureAwait(false);
        return preparation?.TargetServerIds ?? [];
    }

    private async Task<IReadOnlyCollection<int>> ResolveCandidateTargetServerIdsAsync(
        PipelineYamlDefinition definition, int? organizationId, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        foreach (var stage in YamlParsingHelper.FlattenJobs(definition))
        {
            var candidates = await repo.FindCandidateTargetServerIdsAsync(
                stage.Pool, stage.Environment, stage.Agent, OsTypeHelper.Parse(stage.Os),
                organizationId, StageHasDeployStep(stage), ct).ConfigureAwait(false);
            ids.UnionWith(candidates);
        }
        return ids;
    }

    // --- Stage execution, task creation, server resolution, command building, cancel/retry/approval ---
    public async Task AdvanceStageAsync(int pipelineRunId, string completedStageName, CancellationToken ct = default)
    {
        // Per-run lock: serializes concurrent AdvanceStageAsync calls from parallel task completions.
        using var runLock = await _runLocks.AcquireAsync(pipelineRunId, ct).ConfigureAwait(false);
        await AdvanceStageLockedAsync(pipelineRunId, completedStageName, ct).ConfigureAwait(false);
    }

    public async Task ReconcileRunAsync(int pipelineRunId, CancellationToken ct = default)
    {
        using var runLock = await _runLocks.AcquireAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (!await repo.IsRunStillRunningAsync(pipelineRunId, ct).ConfigureAwait(false)) return;
        await AdvanceToNextStageOrCompleteAsync(pipelineRunId, ct).ConfigureAwait(false);
    }

    private async Task AdvanceStageLockedAsync(int pipelineRunId, string completedStageName, CancellationToken ct)
    {
        if (!await repo.IsRunStillRunningAsync(pipelineRunId, ct).ConfigureAwait(false)) return;

        var allDone = await repo.AreAllStepsInStageCompletedAsync(pipelineRunId, completedStageName, ct).ConfigureAwait(false);
        if (!allDone) return;

        var failedSteps = await repo.GetFailedStepRunsInStageAsync(pipelineRunId, completedStageName, ct).ConfigureAwait(false);

        if (await TryRetryFailedStepAsync(pipelineRunId, failedSteps, ct).ConfigureAwait(false))
            return;

        if (HasNonContinuableFailures(failedSteps))
        {
            // User-defined failure/finally stages must run before the pipeline becomes terminal.
            // Otherwise an `always()` teardown remains Pending forever and leaks its environment.
            if (await TryContinueWithFailureHandlersAsync(pipelineRunId, ct).ConfigureAwait(false))
                return;

            // A workspace cleanup is part of the run, not fire-and-forget work after its terminal
            // transition. Keep the run active until the cleanup reports back; its completion will
            // re-enter this scheduler and finalize the run from the original failed step.
            if (await DispatchCleanupIfPendingAsync(pipelineRunId, ct).ConfigureAwait(false))
                return;
            await CompleteRunAsync(pipelineRunId, PipelineStatus.Failed, ct).ConfigureAwait(false);
            return;
        }

        // S-TECH-ARCR: if the completed stage produces artifacts, the next stage may consume them on a
        // DIFFERENT agent. Dispatching the next stage before the collection task finishes lets that
        // consumer resolve an artifact that has not been collected yet ("could not resolve the artifact",
        // run #51). So when a collection task is dispatched, WAIT: the next stage is advanced only once
        // that task completes, routed back through ContinueAfterArtifactCollectionAsync.
        if (await DispatchPostStageArtifactCollectionAsync(pipelineRunId, completedStageName, ct).ConfigureAwait(false))
            return;

        await AdvanceToNextStageOrCompleteAsync(pipelineRunId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-entry point after a post-stage artifact-collection task finishes (S-TECH-ARCR). Advances to the
    /// next stage (or completes the run) under the per-run lock, exactly where <see cref="AdvanceStageLockedAsync"/>
    /// deferred it. Artifact publication is part of the producing stage's contract: a failed, cancelled, or
    /// timed-out collection fails the run instead of allowing a fake green with no consumable artifact.
    /// </summary>
    public async Task ContinueAfterArtifactCollectionAsync(
        int pipelineRunId, TaskExecutionStatus collectionStatus, CancellationToken ct = default)
    {
        using var runLock = await _runLocks.AcquireAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (!await repo.IsRunStillRunningAsync(pipelineRunId, ct).ConfigureAwait(false)) return;

        if (collectionStatus != TaskExecutionStatus.Success)
        {
            await repo.AppendRunWarningsAsync(pipelineRunId,
                [$"Artifact collection ended with status {collectionStatus}; the run cannot publish its declared artifact."],
                ct).ConfigureAwait(false);
            if (await DispatchCleanupIfPendingAsync(pipelineRunId, ct).ConfigureAwait(false))
                return;
            await CompleteRunAsync(pipelineRunId, PipelineStatus.Failed, ct).ConfigureAwait(false);
            return;
        }

        await AdvanceToNextStageOrCompleteAsync(pipelineRunId, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryRetryFailedStepAsync(int pipelineRunId, List<PipelineStepRun> failedSteps, CancellationToken ct)
    {
        var retriable = failedSteps.FirstOrDefault(s => s.RetryCount > 0);
        if (retriable is null) return false;

        retriable.RetryCount--;
        retriable.Status = TaskExecutionStatus.Pending;
        retriable.ExitCode = null;
        retriable.StartedAt = null;
        retriable.CompletedAt = null;
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var retryRun = await repo.GetPipelineRunWithPipelineAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (retryRun?.Pipeline is not null)
        {
            var retryDef = ParseRunDefinition(retryRun);
            if (retryDef is not null)
            {
                await DispatchNextStageWithScopedSecretsAsync(retryRun, retryDef, ct).ConfigureAwait(false);
            }
        }
        return true;
    }

    private static bool HasNonContinuableFailures(List<PipelineStepRun> failedSteps)
    {
        return failedSteps.Count > 0 && !failedSteps.All(s => s.ContinueOnError);
    }

    private async Task<bool> TryContinueWithFailureHandlersAsync(int pipelineRunId, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return false;

        var definition = ParseRunDefinition(run);
        if (definition is null) return false;

        var failureHandlerStages = YamlParsingHelper.FlattenJobs(definition)
            .Where(stage => IsFailureHandlerCondition(stage.Condition))
            .Select(stage => stage.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (failureHandlerStages.Count == 0) return false;

        var pendingSteps = await repo.GetPendingStepRunsAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (!pendingSteps.Any(step => failureHandlerStages.Contains(step.StageName))) return false;

        var blockedSteps = pendingSteps
            .Where(step => step.StageName != SystemCleanupStage && !failureHandlerStages.Contains(step.StageName))
            .ToList();
        if (blockedSteps.Count > 0)
        {
            MarkStepsAs(blockedSteps, TaskExecutionStatus.Cancelled);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await AdvanceToNextStageOrCompleteAsync(pipelineRunId, ct).ConfigureAwait(false);
        return true;
    }

    private static bool IsFailureHandlerCondition(string? condition)
    {
        var normalized = condition?.Trim().ToLowerInvariant();
        return normalized is "always()" or "failed()";
    }

    private async Task AdvanceToNextStageOrCompleteAsync(int pipelineRunId, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return;

        var definition = ParseRunDefinition(run);
        if (definition is null) return;

        var (resolvedVars, secretKeys) = await ResolveFullVariablesWithSecretsForRunAsync(run, definition, ct).ConfigureAwait(false);

        var pipelineIdForRun = await repo.GetPipelineIdForRunAsync(pipelineRunId, ct).ConfigureAwait(false);
        var organizationId = pipelineIdForRun is null
            ? null
            : await repo.GetPipelineOrganizationIdAsync(pipelineIdForRun.Value, ct).ConfigureAwait(false);

        // Loop: CreateTasksForNextStageAsync may cancel stages (condition=false), which unblocks
        // downstream stages. Keep re-evaluating until either a task is dispatched or no pending
        // steps remain.
        for (var guard = 0; guard < 10; guard++)
        {
            if (guard > 0 && !await IsRunActiveAsync(pipelineRunId, ct).ConfigureAwait(false)) return;

            var pendingSteps = await repo.GetPendingStepRunsAsync(pipelineRunId, ct).ConfigureAwait(false);
            if (pendingSteps.Count == 0)
            {
                var finalStatus = await repo.HasAnyFailedStepInRunAsync(pipelineRunId, ct).ConfigureAwait(false)
                    ? PipelineStatus.Failed
                    : PipelineStatus.Success;
                await CompleteRunAsync(pipelineRunId, finalStatus, ct).ConfigureAwait(false);
                if (finalStatus == PipelineStatus.Success)
                    await pipelineHub.Clients.Groups(HubGroups.PipelineRunUpdates(pipelineRunId, run.Pipeline.Id)).SendAsync("PipelineRunCompleted", pipelineRunId, PipelineStatus.Success, ct).ConfigureAwait(false);
                return;
            }

            var hadCancelled = await CreateTasksForNextStageAsync(pipelineRunId, definition, resolvedVars, pendingSteps, organizationId, secretKeys, ct).ConfigureAwait(false);
            if (!hadCancelled) return;
        }
    }

    private async Task CompleteRunAsync(int pipelineRunId, PipelineStatus status, CancellationToken ct)
    {
        await repo.UpdatePipelineRunStatusAsync(pipelineRunId, status, ct).ConfigureAwait(false);
        await domainEvents.DispatchAsync(new PipelineRunCompletedEvent(pipelineRunId, status), ct).ConfigureAwait(false);
        secretMasking.EvictCache(pipelineRunId);

        if (status == PipelineStatus.Failed)
        {
            var pipelineId = await repo.GetPipelineIdForRunAsync(pipelineRunId, ct).ConfigureAwait(false);
            var groups = HubGroups.PipelineRunUpdates(pipelineRunId, pipelineId);
            await pipelineHub.Clients.Groups(groups).SendAsync("PipelineRunCompleted", pipelineRunId, PipelineStatus.Failed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Returns true if any stage was cancelled (condition=false), signaling the caller to re-evaluate.</summary>
    // First-dispatch convenience: the caller already resolved (resolvedVars, secretKeys) - reuse them (so
    // run parameters/additionalVars are preserved) while still scoping secrets per step. Computes the
    // pending steps + owning org, then defers to the scoping overload.
    private async Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        HashSet<string> secretKeys, CancellationToken ct = default)
    {
        var pendingSteps = await repo.GetPendingStepRunsAsync(runId, ct).ConfigureAwait(false);
        var pipelineIdForRun = await repo.GetPipelineIdForRunAsync(runId, ct).ConfigureAwait(false);
        var organizationId = pipelineIdForRun is null
            ? null
            : await repo.GetPipelineOrganizationIdAsync(pipelineIdForRun.Value, ct).ConfigureAwait(false);
        return await CreateTasksForNextStageAsync(runId, definition, resolvedVars, pendingSteps, organizationId, secretKeys, ct).ConfigureAwait(false);
    }

    // Retry / resume dispatch WITH the same per-step secret scoping as the first dispatch: resolve the
    // secret-key set so each step's task env carries only the secrets it references, instead of the full
    // secret set. (Previously these paths re-injected every secret into every step - over-exposure.)
    private async Task DispatchNextStageWithScopedSecretsAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
    {
        var (resolvedVars, secretKeys) = await ResolveFullVariablesWithSecretsForRunAsync(run, definition, ct).ConfigureAwait(false);
        var pendingSteps = await repo.GetPendingStepRunsAsync(run.Id, ct).ConfigureAwait(false);
        var pipelineIdForRun = await repo.GetPipelineIdForRunAsync(run.Id, ct).ConfigureAwait(false);
        var organizationId = pipelineIdForRun is null
            ? null
            : await repo.GetPipelineOrganizationIdAsync(pipelineIdForRun.Value, ct).ConfigureAwait(false);
        await CreateTasksForNextStageAsync(run.Id, definition, resolvedVars, pendingSteps, organizationId, secretKeys, ct).ConfigureAwait(false);
    }

    private async Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        List<PipelineStepRun> pendingSteps, int? organizationId, HashSet<string> secretKeys, CancellationToken ct = default)
    {
        var completedStages = await repo.GetCompletedStageNamesAsync(runId, ct).ConfigureAwait(false);
        var terminalStages = await repo.GetTerminalStageNamesAsync(runId, ct).ConfigureAwait(false);

        var previousStageFailed = await repo.HasAnyFailedStepInRunAsync(runId, ct).ConfigureAwait(false);
        var dependencyTerminalStages = previousStageFailed
            ? terminalStages
            : completedStages;

        if (pendingSteps.Count == 0)
        {
            if (await IsRunActiveAsync(runId, ct).ConfigureAwait(false))
                await FailRunWithUnmatchedStagesAsync(runId,
                    ["Pipeline has no executable steps - every step was skipped or cancelled."],
                    [], ct).ConfigureAwait(false);
            return false;
        }

        var flattenedStages = YamlParsingHelper.FlattenJobs(definition);
        var readyStageNames = FindReadyStages(pendingSteps, definition, completedStages, dependencyTerminalStages);
        if (readyStageNames.Count == 0)
        {
            if (await repo.HasAnyRunningStepInRunAsync(runId, ct).ConfigureAwait(false))
                return false;

            // A synchronous failure (for example an immutable artifact that cannot be resolved)
            // has no task completion left to cancel its dependent tail. Settle that blocked tail,
            // but preserve explicit failure handlers and System:Cleanup; the caller immediately
            // re-evaluates and dispatches those stages in dependency order.
            if (previousStageFailed)
            {
                var failureHandlerStages = flattenedStages
                    .Where(stage => IsFailureHandlerCondition(stage.Condition))
                    .Select(stage => stage.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var blockedAfterFailure = pendingSteps
                    .Where(step => step.StageName != SystemCleanupStage
                        && !failureHandlerStages.Contains(step.StageName))
                    .ToList();
                if (blockedAfterFailure.Count > 0)
                {
                    MarkStepsAs(blockedAfterFailure, TaskExecutionStatus.Cancelled);
                    await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                    return true;
                }
            }

            if (await IsRunActiveAsync(runId, ct).ConfigureAwait(false))
                await FailRunWithUnmatchedStagesAsync(runId,
                    BuildDeadlockReasons(pendingSteps, definition, completedStages),
                    pendingSteps, ct).ConfigureAwait(false);
            return false;
        }

        var anyTaskCreated = false;
        var anyStageCancelled = false;
        var anyStageSyncFailed = false;
        var anyThrottled = false;
        var anyRunnerTemporarilyUnavailable = false;
        var anyEnvCheckFailed = false;
        var systemTaskDispatchFailed = false;
        var unmatchedReasons = new List<string>();
        var unmatchedStageSteps = new List<PipelineStepRun>();

        var affinityServerId = await repo.GetRunAffinityServerIdAsync(runId, ct).ConfigureAwait(false);

        // Output variables are run-scoped and may drive the condition of the next stage. They must
        // therefore be available before conditions are evaluated, not only while creating its tasks.
        await InjectOutputVariablesAsync(runId, resolvedVars, ct).ConfigureAwait(false);

        // S-TECH-56: snapshot the flattened stages and the currently in-flight stage names once, so
        // MaxParallel can cap how many jobs of the same stage group are dispatched concurrently.
        var activeStageNames = await repo.GetActiveStageNamesAsync(runId, ct).ConfigureAwait(false) ?? [];
        var dispatchedPerGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var stageName in readyStageNames)
        {
            var stageSteps = pendingSteps.Where(s => s.StageName == stageName).ToList();

            // Handle system tasks (Prepare/Cleanup) - they have no YAML stage definition
            if (stageName is SystemPrepareStage or SystemCleanupStage)
            {
                var dispatched = await CreateSystemTasksAsync(
                    runId, stageSteps, resolvedVars, definition, organizationId, completedStages, ct).ConfigureAwait(false);
                if (dispatched)
                {
                    anyTaskCreated = true;
                }
                else
                {
                    // A heartbeat timeout can mark the only configured runner offline for a few seconds.
                    // Keep Prepare pending when its target still exists: PipelineTriggerReconcileService
                    // will re-drive it after SchedulerRecoveryGrace. A missing target remains a hard
                    // configuration error. Cleanup intentionally keeps its stricter affinity handling.
                    var firstStage = flattenedStages.FirstOrDefault();
                    var configuredTargets = stageName == SystemPrepareStage && firstStage is not null
                        ? await repo.FindCandidateTargetServerIdsAsync(
                            firstStage.Pool, firstStage.Environment, firstStage.Agent,
                            OsTypeHelper.Parse(firstStage.Os), organizationId, false, ct).ConfigureAwait(false)
                        : [];
                    if (configuredTargets.Count > 0)
                    {
                        anyRunnerTemporarilyUnavailable = true;
                        logger.LogWarning(
                            "Run {RunId} system stage {StageName} is waiting for a configured runner to come back online",
                            runId, stageName);
                    }
                    else
                    {
                        systemTaskDispatchFailed = true;
                        unmatchedReasons.Add($"System stage '{stageName}': no online pipeline runner is available.");
                        unmatchedStageSteps.AddRange(stageSteps);
                    }
                }
                continue;
            }

            var stageDef = flattenedStages.FirstOrDefault(s => s.Name == stageName);
            if (stageDef is null) continue;

            if (!EvaluateCondition(stageDef.Condition, resolvedVars, completedStages, previousStageFailed))
            {
                MarkStepsAs(stageSteps, TaskExecutionStatus.Cancelled);
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                anyStageCancelled = true;
                continue;
            }

            // S-TECH-56: cap concurrent jobs within a stage group when it opts into a deployment
            // strategy. Throttled jobs stay Pending and are re-evaluated when an in-flight sibling
            // completes (each job is its own pseudo-stage, so its completion re-triggers scheduling).
            if (IsGroupThrottled(stageDef, flattenedStages, activeStageNames, dispatchedPerGroup))
            {
                anyThrottled = true;
                continue;
            }

            if (await CheckAndCreateApprovalAsync(runId, stageName, stageDef, ct).ConfigureAwait(false))
                return false;

            if (await CheckEnvironmentChecksAsync(stageDef, ct).ConfigureAwait(false) == false)
            {
                MarkStepsAs(stageSteps, TaskExecutionStatus.Failed);
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                anyEnvCheckFailed = true;
                continue;
            }

            // A deploy stage targets a deployment-capable host (the deploy target B), NOT the build
            // server A - so it must be EXCLUDED from build affinity and resolved fail-closed against
            // DeploymentTargetAvailable servers only (no plain-runner fallback).
            var stageHasDeploy = StageHasDeployStep(stageDef);

            Server? server = null;
            if (!stageHasDeploy && affinityServerId is not null && string.IsNullOrEmpty(stageDef.Agent) && string.IsNullOrEmpty(stageDef.Pool) && string.IsNullOrEmpty(stageDef.Environment))
                // Affinity must still honor the stage's OS requirement - never pin a typed stage to
                // an incompatible server just because an earlier (untyped) stage ran there.
                server = await repo.FindOnlineServerByIdAsync(affinityServerId.Value, OsTypeHelper.Parse(stageDef.Os), ct).ConfigureAwait(false);
            server ??= stageHasDeploy
                ? await repo.FindOnlineDeployTargetAsync(stageDef.Pool, stageDef.Environment, stageDef.Agent, OsTypeHelper.Parse(stageDef.Os), organizationId, ct).ConfigureAwait(false)
                : await ResolveServerAsync(stageDef, organizationId, ct).ConfigureAwait(false);
            if (server is null)
            {
                var configuredTargets = await repo.FindCandidateTargetServerIdsAsync(
                    stageDef.Pool, stageDef.Environment, stageDef.Agent, OsTypeHelper.Parse(stageDef.Os),
                    organizationId, stageHasDeploy, ct).ConfigureAwait(false);
                if (configuredTargets.Count > 0)
                {
                    anyRunnerTemporarilyUnavailable = true;
                    logger.LogWarning(
                        "Run {RunId} stage {StageName} is waiting for a configured {TargetKind} to come back online",
                        runId, stageName, stageHasDeploy ? "deployment target" : "pipeline runner");
                }
                else
                {
                    unmatchedReasons.Add(stageHasDeploy ? BuildNoDeployTargetReason(stageName, stageDef) : BuildNoServerReason(stageName, stageDef));
                    unmatchedStageSteps.AddRange(stageSteps);
                }
                continue;
            }

            // Fail-closed isolation enforcement: a stage requesting container isolation only runs
            // on a Docker-capable runner, and a "containers only" runner refuses process-mode steps.
            // Never silently downgrade - block the run with a concrete reason instead.
            var isolationViolation = CheckIsolationPolicy(stageName, stageDef, server);
            if (isolationViolation is not null)
            {
                unmatchedReasons.Add(isolationViolation);
                unmatchedStageSteps.AddRange(stageSteps);
                continue;
            }

            // Belt-and-suspenders: a deploy stage may only run on a deploy-capable server. The deploy
            // resolver already guarantees this, but the affinity/explicit paths above could not - fail
            // closed with a clear reason rather than dispatching a deploy to a non-deploy host.
            var deploymentViolation = CheckDeploymentPolicy(stageName, stageHasDeploy, server);
            if (deploymentViolation is not null)
            {
                unmatchedReasons.Add(deploymentViolation);
                unmatchedStageSteps.AddRange(stageSteps);
                continue;
            }

            // A deploy stage's server must never become the build affinity for later build stages.
            if (!stageHasDeploy) affinityServerId ??= server.Id;

            // A fail-closed matrix-leg / isolation violation inside the stage finalizes the run itself;
            // stop here so the post-loop logic never re-fails an already-terminal run (double broadcast).
            if (await CreateTasksForStageAsync(runId, server, stageSteps, stageDef, resolvedVars, completedStages, previousStageFailed, secretKeys, organizationId, ct).ConfigureAwait(false))
                return false;

            // A stage advances the run only through an in-flight unit of work: an agent ServerTask (step
            // left Assigned) or a trigger step waiting on its child run (left Running). Steps that failed
            // SYNCHRONOUSLY at dispatch (a trigger/deploy/certbot step whose pre-flight rejected it, or a
            // snapshot-drift step with no YAML) leave NOTHING in flight - counting them as "dispatched"
            // strands the run in Running until the 1h backstop. Only credit real in-flight work.
            if (stageSteps.Any(s => s.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned or TaskExecutionStatus.Running))
            {
                anyTaskCreated = true;
                if (!string.IsNullOrEmpty(stageDef.Group))
                    dispatchedPerGroup[stageDef.Group] = dispatchedPerGroup.GetValueOrDefault(stageDef.Group) + 1;
            }
            else if (stageSteps.Any(s => s.Status is TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout))
            {
                // Stage settled synchronously with a failure and nothing in flight: no task completion will
                // ever re-enter AdvanceStageAsync, so finalize the run now instead of stranding it. The
                // failed step already carries its own warning (dispatched by the *StepAsync FailAsync path).
                anyStageSyncFailed = true;
            }
            else
            {
                // Stage produced only Cancelled (per-step condition=false) / non-failed terminal steps and
                // nothing in flight. Persist those terminal statuses NOW (mirrors the stage-level skip
                // above): GetPendingStepRunsAsync filters on Status==Pending in the DB, so leaving the
                // cancellations unflushed makes the re-evaluation re-fetch the same steps every pass -
                // the run would spin the guard loop and never converge. Then re-evaluate (advance/complete)
                // exactly like a stage-level condition skip, rather than wrongly failing the run below.
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                anyStageCancelled = true;
            }
        }

        // CreateSystemTasksAsync can fail the run itself for an isolation violation. Do not apply a
        // second terminal transition or broadcast after that synchronous failure.
        if (systemTaskDispatchFailed && !await IsRunActiveAsync(runId, ct).ConfigureAwait(false))
            return false;

        if (anyTaskCreated)
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        // A stage failed synchronously with nothing left in flight (e.g. a trigger step the F-EXEC-1b gate
        // rejected). Persist the terminal step, then re-enter the scheduler immediately: failure handlers
        // and System:Cleanup still belong to the run and must execute before its terminal transition.
        if (anyStageSyncFailed)
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }

        if (anyStageCancelled)
            return true; // signal caller to re-evaluate with updated state

        // S-TECH-56: nothing was dispatched only because MaxParallel held the ready jobs back. Their
        // in-flight siblings will re-trigger scheduling on completion - do NOT fail the run here.
        if ((anyThrottled || anyRunnerTemporarilyUnavailable) && unmatchedReasons.Count == 0 && !anyEnvCheckFailed)
            return false;

        await FailRunWithUnmatchedStagesAsync(runId, unmatchedReasons, unmatchedStageSteps, ct).ConfigureAwait(false);
        return false;
    }

    // A pipeline needs the workspace (clone/checkout) only if at least one step is NOT a `type: trigger`
    // step. Trigger steps just launch a child run; a pipeline made entirely of them (a pure orchestrator)
    // needs no System:Prepare/Cleanup. Drives both the injection skip and the readiness gate above.
    private static bool RequiresWorkspace(PipelineYamlDefinition definition)
        => YamlParsingHelper.FlattenJobs(definition)
            .Any(stage => stage.Steps.Any(step => !string.Equals(step.Type, "trigger", StringComparison.OrdinalIgnoreCase)));

    private static List<string> FindReadyStages(
        List<PipelineStepRun> pendingSteps, PipelineYamlDefinition definition,
        List<string> completedStages, List<string> terminalStages)
    {
        // A trigger-only pipeline injects no System:Prepare (see RequiresWorkspace), so its user stages
        // must not wait on a prepare stage that will never complete - treat prepare as done in that case.
        var prepareCompleted = !RequiresWorkspace(definition) || completedStages.Contains(SystemPrepareStage);
        // Flatten once: re-flattening the YAML inside the Where lambda cost a full
        // stages>jobs walk per pending stage on every scheduling pass.
        var flattenedStages = YamlParsingHelper.FlattenJobs(definition);
        // Cleanup must wait for every user stage to reach a terminal state, not for every stage to
        // succeed. A failed stage followed by an always()/failed() teardown is a normal failure path:
        // once the remaining stages are cancelled or settled, the workspace still has to be removed.
        var allUserStagesTerminal = flattenedStages.All(s => terminalStages.Contains(s.Name));

        return pendingSteps
            .Select(s => s.StageName)
            .Distinct()
            .Where(stageName =>
            {
                if (stageName == SystemPrepareStage) return true;
                if (stageName == SystemCleanupStage) return allUserStagesTerminal;
                if (!prepareCompleted) return false;
                var stageDef = flattenedStages.FirstOrDefault(s => s.Name == stageName);
                if (stageDef is null) return false;
                var satisfiedDependencies = IsFailureHandlerCondition(stageDef.Condition)
                    ? terminalStages
                    : completedStages;
                return stageDef.DependsOn.Count == 0 || stageDef.DependsOn.All(satisfiedDependencies.Contains);
            })
            .ToList();
    }

    private void MarkStepsAs(List<PipelineStepRun> steps, TaskExecutionStatus status)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var step in steps)
        {
            step.Status = status;
            step.CompletedAt = now;
        }
    }

    // Guards the dead-end failure branches so a non-Running run (Cancelled / WaitingForApproval /
    // already completed) is never flipped to Failed by a late scheduling pass.
    private async Task<bool> IsRunActiveAsync(int runId, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        return run?.Status == PipelineStatus.Running;
    }

    private async Task FailRunWithUnmatchedStagesAsync(
        int runId, List<string> unmatchedReasons, List<PipelineStepRun> unmatchedStageSteps, CancellationToken ct)
    {
        if (unmatchedReasons.Count > 0)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var step in unmatchedStageSteps)
            {
                step.Status = TaskExecutionStatus.Failed;
                step.StartedAt ??= now;
                step.CompletedAt = now;
            }
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            // R-04: persist the human-readable "no server matched" reason. This MUST go through a
            // tracked repository update - GetPipelineRunWithPipelineAsync returns an AsNoTracking
            // entity, so mutating its WarningsJson here would be silently dropped on SaveChanges,
            // leaving the run Failed with no visible error message for the user.
            await repo.AppendRunWarningsAsync(runId, unmatchedReasons, ct).ConfigureAwait(false);
        }

        // Flush any tracked step-status changes (e.g. a step that failed synchronously at dispatch and
        // was never saved) BEFORE UpdatePipelineRunStatusAsync: its Failed-status safety net queries the
        // DB for a failed step, and a still-tracked-but-unflushed Failed step would be invisible to it,
        // risking a wrongful override back to Success. Saving here makes the terminal state authoritative.
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await repo.UpdatePipelineRunStatusAsync(runId, PipelineStatus.Failed, ct).ConfigureAwait(false);
        var pipelineId = await repo.GetPipelineIdForRunAsync(runId, ct).ConfigureAwait(false);
        var groups = HubGroups.PipelineRunUpdates(runId, pipelineId);
        await pipelineHub.Clients.Groups(groups).SendAsync("PipelineRunCompleted", runId, PipelineStatus.Failed, ct).ConfigureAwait(false);
    }

    // Human-readable fail-closed reason. Explicit selectors never broaden to another runner.
    private static string BuildNoServerReason(string stageName, PipelineStageDefinition stageDef)
    {
        var target = !string.IsNullOrEmpty(stageDef.Pool) ? $"pool '{stageDef.Pool}'"
            : !string.IsNullOrEmpty(stageDef.Environment) ? $"environment '{stageDef.Environment}'"
            : !string.IsNullOrEmpty(stageDef.Agent) ? $"agent '{stageDef.Agent}'"
            : "default";
        var osNote = !string.IsNullOrEmpty(stageDef.Os) ? $", os '{stageDef.Os}'" : string.Empty;
        return $"Stage '{stageName}': no online pipeline runner available in the organization (requested {target}{osNote}).";
    }

    // Only the native cross-agent `type: deploy` operation requires the deployment module.
    // `execution_role: deploy` is a workload classification used by deployment-only runners; shell,
    // artifact and other ordinary pipeline steps still require a pipeline runner, not the unrelated
    // cross-agent deploy helper installed by `--module deployment`.
    private static bool StageHasDeployStep(PipelineStageDefinition stageDef)
        => stageDef.Steps.Any(s => string.Equals(s.Type, "deploy", StringComparison.OrdinalIgnoreCase));

    private static string BuildNoDeployTargetReason(string stageName, PipelineStageDefinition stageDef)
    {
        var target = !string.IsNullOrEmpty(stageDef.Pool) ? $"pool '{stageDef.Pool}'"
            : !string.IsNullOrEmpty(stageDef.Environment) ? $"environment '{stageDef.Environment}'"
            : !string.IsNullOrEmpty(stageDef.Agent) ? $"agent '{stageDef.Agent}'"
            : "default";
        var osNote = !string.IsNullOrEmpty(stageDef.Os) ? $", os '{stageDef.Os}'" : string.Empty;
        return $"Deploy stage '{stageName}': no online deployment-capable agent available (requested {target}{osNote}). "
             + "Install an agent with --module deployment, or target a host that has the deployment capability.";
    }

    // Fail-closed deploy policy (mirrors CheckIsolationPolicy): a deploy stage may only run on a
    // server whose deployment capability is present. Returns a concrete reason on violation, else null.
    private static string? CheckDeploymentPolicy(string stageName, bool stageHasDeploy, Server server)
        => stageHasDeploy && !server.DeploymentTargetAvailable
            ? $"Deploy stage '{stageName}': server '{server.Name}' is not a deployment target (missing the deployment capability)."
            : null;

    // P-04 / P-01: Server resolution - pool > environment > agent name. Explicit selectors are
    // fail-closed; only stages with no selector may use an organization-scoped runner fallback.
    private async Task<Server?> ResolveServerAsync(PipelineStageDefinition stageDef, int? organizationId, CancellationToken ct)
    {
        var requiredOs = OsTypeHelper.Parse(stageDef.Os);
        Server? server = null;

        if (!string.IsNullOrEmpty(stageDef.Pool))
            server = organizationId is { } poolOrg
                ? await repo.FindOnlineServerInPoolInOrganizationAsync(stageDef.Pool, requiredOs, poolOrg, ct).ConfigureAwait(false)
                : await repo.FindOnlineServerInPoolAsync(stageDef.Pool, requiredOs, ct).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(stageDef.Environment))
            server = organizationId is { } environmentOrg
                ? await repo.FindOnlineServerInEnvironmentInOrganizationAsync(stageDef.Environment, requiredOs, environmentOrg, ct).ConfigureAwait(false)
                : await repo.FindOnlineServerInEnvironmentAsync(stageDef.Environment, requiredOs, ct).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(stageDef.Agent) && !stageDef.Agent.Equals("default", StringComparison.OrdinalIgnoreCase))
            server = organizationId is { } agentOrg
                ? await repo.FindOnlineServerByAgentInOrganizationAsync(stageDef.Agent, requiredOs, agentOrg, ct).ConfigureAwait(false)
                : await repo.FindOnlineServerByAgentAsync(stageDef.Agent, requiredOs, ct).ConfigureAwait(false);

        // A globally named pool/environment/agent must never cross the pipeline's organization boundary.
        if (server is not null && organizationId is { } orgId && server.OrganizationId != orgId)
            server = null;

        var hasExplicitSelector = !string.IsNullOrEmpty(stageDef.Pool)
            || !string.IsNullOrEmpty(stageDef.Environment)
            || !string.IsNullOrEmpty(stageDef.Agent) && !stageDef.Agent.Equals("default", StringComparison.OrdinalIgnoreCase);
        if (hasExplicitSelector) return server;

        return await repo.FindAnyOnlineRunnerAsync(organizationId, requiredOs, ct).ConfigureAwait(false);
    }

    // --- P-14: Pipeline Run Cancellation ---

    public async Task<bool> CancelRunAsync(int runId, CancellationToken ct = default)
    {
        var root = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (root is null || IsTerminal(root.Status)) return false;
        if (!await CancelSingleRunAsync(root, ct).ConfigureAwait(false)) return false;

        var visited = new HashSet<int> { runId };
        var pendingParents = new Queue<int>();
        pendingParents.Enqueue(runId);
        while (pendingParents.TryDequeue(out var parentRunId))
        {
            var childIds = await repo.GetTriggeredChildRunIdsAsync(parentRunId, ct).ConfigureAwait(false);
            foreach (var childId in childIds)
            {
                if (!visited.Add(childId)) continue;
                var child = await repo.GetPipelineRunWithPipelineAsync(childId, ct).ConfigureAwait(false);
                if (child is not null && !IsTerminal(child.Status))
                    await CancelSingleRunAsync(child, ct).ConfigureAwait(false);
                pendingParents.Enqueue(childId);
            }
        }

        return true;
    }

    private async Task<bool> CancelSingleRunAsync(PipelineRun run, CancellationToken ct)
    {
        if (!await repo.TryTransitionPipelineRunStatusAsync(
                run.Id, run.Status, PipelineStatus.Cancelled, ct).ConfigureAwait(false))
            return false;
        await repo.CancelPendingStepRunsAsync(run.Id, ct).ConfigureAwait(false);
        secretMasking.EvictCache(run.Id);
        var cancelGroups = HubGroups.PipelineRunUpdates(run.Id, run.Pipeline?.Id);
        await pipelineHub.Clients.Groups(cancelGroups)
            .SendAsync("PipelineRunCancelled", run.Id, ct).ConfigureAwait(false);
        return true;
    }

    private static bool IsTerminal(PipelineStatus status) =>
        status is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled;

    public async Task FailStuckRunAsync(int runId, CancellationToken ct = default)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run is null || run.Status is not PipelineStatus.Running) return; // already terminal or gone

        // Cancel the pending tail so no orphan steps linger, then finalize Failed through the normal
        // completion path (dispatches PipelineRunCompletedEvent -> unblocks any parent trigger step, and
        // broadcasts PipelineRunCompleted so the UI/top-bar drop the spinning run live).
        await repo.CancelPendingStepRunsAsync(runId, ct).ConfigureAwait(false);
        await CompleteRunAsync(runId, PipelineStatus.Failed, ct).ConfigureAwait(false);
    }

    public async Task<bool> ResumeAfterApprovalAsync(int runId, CancellationToken ct = default)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Status != PipelineStatus.WaitingForApproval || run.Pipeline is null) return false; // F-017: align the null-guard with sibling methods

        if (!await repo.TryTransitionPipelineRunStatusAsync(
                run.Id, PipelineStatus.WaitingForApproval, PipelineStatus.Running, ct).ConfigureAwait(false))
            return false;

        var definition = ParseRunDefinition(run);
        if (definition is not null)
        {
            await DispatchNextStageWithScopedSecretsAsync(run, definition, ct).ConfigureAwait(false);
        }
        return true;
    }

    public async Task<PipelineRunDto?> RetryFailedStepsAsync(int runId, CancellationToken ct = default)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null || run.Status != PipelineStatus.Failed) return null;

        // Repository re-opens the run (Status → Running, CompletedAt → null) and resets the
        // failed step runs to Pending atomically. Zero reset → nothing to retry.
        var reset = await repo.ResetFailedStepRunsAsync(runId, ct).ConfigureAwait(false);
        if (reset == 0) return null;

        var definition = ParseRunDefinition(run);
        if (definition is not null)
        {
            await DispatchNextStageWithScopedSecretsAsync(run, definition, ct).ConfigureAwait(false);
        }

        await pipelineHub.Clients
            .Groups(HubGroups.PipelineRunUpdates(runId, run.Pipeline.Id))
            .SendAsync("PipelineRunStarted", runId, run.Pipeline.Id, ct).ConfigureAwait(false);

        return await GetRunAsync(runId, ct).ConfigureAwait(false);
    }

    private async Task<bool> CheckAndCreateApprovalAsync(
        int runId, string stageName, PipelineStageDefinition stageDef, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stageDef.Environment)) return false;

        var env = await repo.FindEnvironmentByNameAsync(stageDef.Environment, ct).ConfigureAwait(false);
        if (env is null || !env.RequireApproval) return false;

        // Check if an approval already exists for this run/stage
        var existingApprovals = await repo.GetApprovalsAsync(runId, ct).ConfigureAwait(false);
        if (existingApprovals.Any(a => a.StageName == stageName)) return false;

        var approval = new PipelineApproval
        {
            PipelineRunId = runId,
            StageName = stageName,
            EnvironmentId = env.Id
        };
        await repo.AddApprovalAsync(approval, ct).ConfigureAwait(false);

        if (!await repo.TryTransitionPipelineRunStatusAsync(
                runId, PipelineStatus.Running, PipelineStatus.WaitingForApproval, ct).ConfigureAwait(false))
            return false;
        var approvalPipelineId = await repo.GetPipelineIdForRunAsync(runId, ct).ConfigureAwait(false);
        var approvalGroups = HubGroups.PipelineRunUpdates(runId, approvalPipelineId);
        await pipelineHub.Clients.Groups(approvalGroups).SendAsync("ApprovalRequired", runId, stageName, env.Name, ct).ConfigureAwait(false);
        // Approval observers (audit, notifications) are non-blocking.
        domainEvents.Publish(new PipelineApprovalRequestedEvent(runId, stageName, env.Name));

        return true;
    }

    // --- P-22: Environment Checks ---

    private async Task<bool> CheckEnvironmentChecksAsync(PipelineStageDefinition stageDef, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stageDef.Environment)) return true;

        var env = await repo.FindEnvironmentByNameAsync(stageDef.Environment, ct).ConfigureAwait(false);
        if (env is null) return true;

        var checks = await repo.GetEnvironmentChecksAsync(env.Id, ct).ConfigureAwait(false);
        if (checks.Count == 0) return true;

        foreach (var check in checks.Where(c => c.IsRequired))
        {
            var passed = check.Type switch
            {
                EnvironmentCheckType.RestCallback => await EvaluateRestCheckAsync(check, ct).ConfigureAwait(false),
                _ => false
            };

            if (!passed)
            {
                logger.LogWarning("Environment check '{CheckName}' failed for environment '{EnvName}'", check.Name, env.Name);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> EvaluateRestCheckAsync(EnvironmentCheck check, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(check.Configuration)) return false;

        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(check.Configuration);
            if (config is null || !config.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url)) return false;

            // F-17: SSRF guard - only http/https, no redirects (handled by HttpClient policy),
            // and reject loopback / RFC1918 / link-local hosts so callbacks can't probe internal services.
            if (!IsSafeOutboundUrl(url))
            {
                logger.LogWarning("REST callback check '{CheckName}' rejected unsafe URL '{Url}'.", check.Name, url);
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(check.TimeoutSeconds));

            // F-17 (hardening): the literal check above only sees the host string. Resolve a DNS host and
            // reject if ANY resolved address is internal, closing the "DNS name that resolves to an internal
            // / cloud-metadata IP (e.g. 169.254.169.254)" hole. A narrow TOCTOU window remains if the name
            // re-resolves between here and connect; these checks are intended for trusted hosts only.
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !System.Net.IPAddress.TryParse(parsed.Host, out _))
            {
                var resolved = await System.Net.Dns.GetHostAddressesAsync(parsed.Host, timeoutCts.Token).ConfigureAwait(false);
                if (resolved.Length == 0 || Array.Exists(resolved, IsBlockedIp))
                {
                    logger.LogWarning("REST callback check '{CheckName}' rejected URL '{Url}' - host resolves to an internal address.", check.Name, url);
                    return false;
                }
            }

            var httpClient = httpClientFactory.CreateClient(PipelinesModuleExtensions.EnvironmentCheckHttpClient);
            using var response = await httpClient.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.Net.Sockets.SocketException)
        {
            logger.LogWarning(ex, "REST callback check '{CheckName}' failed", check.Name);
            return false;
        }
    }

    private static bool IsSafeOutboundUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        // Reject by host name pattern first (covers DNS-only entries before resolution).
        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;

        if (System.Net.IPAddress.TryParse(host, out var ip) && IsBlockedIp(ip)) return false;
        return true;
    }

    // Shared internal-address predicate used both for a literal-IP host and for each DNS-resolved address.
    private static bool IsBlockedIp(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, 0.0.0.0/8, 100.64.0.0/10 (CGNAT)
            if (bytes[0] is 10 or 0) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (ULA) - IsIPv6SiteLocal only covers the deprecated fec0::/10.
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if ((ip.GetAddressBytes()[0] & 0xFE) == 0xFC) return true;
            // An IPv4-mapped IPv6 address (::ffff:a.b.c.d) must be judged on its embedded IPv4.
            if (ip.IsIPv4MappedToIPv6 && IsBlockedIp(ip.MapToIPv4())) return true;
        }
        return false;
    }

    // --- ServerTask creation & dispatch (per-stage steps, system tasks, artifact collection) ---
    /// <returns><c>true</c> if an artifact-collection task was dispatched (the caller must then wait for it
    /// before advancing, S-TECH-ARCR); <c>false</c> if the stage has no artifacts / no resolvable server.</returns>
    private async Task<bool> DispatchPostStageArtifactCollectionAsync(int runId, string stageName, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return false;

        var definition = ParseRunDefinition(run);
        if (definition is null) return false;

        var stageDef = YamlParsingHelper.FlattenJobs(definition).FirstOrDefault(s => s.Name == stageName);
        if (stageDef is null || stageDef.Artifacts.Count == 0) return false;

        var organizationId = await repo.GetPipelineOrganizationIdAsync(run.Pipeline.Id, ct).ConfigureAwait(false);
        var producerServerId = await repo.GetStageProducerServerIdAsync(runId, stageName, ct).ConfigureAwait(false);
        if (producerServerId is null) return false;
        var server = await repo.FindOnlineServerByIdAsync(producerServerId.Value, ct: ct).ConfigureAwait(false);
        if (server is null || organizationId is not null && server.OrganizationId != organizationId.Value) return false;
        if (server is null) return false;

        var resolvedVars = await ResolveFullVariablesForRunAsync(run, definition, ct).ConfigureAwait(false);
        InjectStageSystemVariables(resolvedVars, stageName, server);
        var isWindows = OsTypeHelper.IsWindows(server.OsType, server.OsDescription);
        if (isWindows && resolvedVars.TryGetValue("WORKSPACE", out var ws) && !ws.Contains(":\\"))
            resolvedVars["WORKSPACE"] = PipelineCommandBuilder.GetDefaultWorkspace(runId, isWindows: true);
        var workspace = resolvedVars.GetValueOrDefault("WORKSPACE", "");

        var resolvedPatterns = stageDef.Artifacts
            .Select(p => SubstituteVariables(p, resolvedVars))
            .ToList();

        var artifactVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_ARTIFACT_NAME"] = $"{stageName}-artifacts",
            ["AETHEUS_RUN_ID"] = runId.ToString(),
            ["AETHEUS_STAGE_NAME"] = stageName,
            ["AETHEUS_WORKING_DIR"] = workspace
        };

        var task = new ServerTask
        {
            ServerId = server.Id,
            Name = $"Collect Artifacts ({stageName})",
            Command = JsonSerializer.Serialize(resolvedPatterns),
            PipelineRunId = runId,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(artifactVars)),
            TimeoutSeconds = PipelineCommandBuilder.ArtifactCollectionTimeoutSeconds,
            Operation = OperationKind.PipelineCollectArtifacts
        };
        repo.TrackTask(task);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Dispatched artifact collection task for run {RunId} stage {Stage}", runId, stageName);
        return true;
    }

    // Returns true when this stage ALREADY finalized the run (a fail-closed matrix-leg / isolation
    // violation calls FailRunWithUnmatchedStagesAsync itself), so the caller must not re-fail it.
    private async Task<bool> CreateTasksForStageAsync(
        int runId, Server server, List<PipelineStepRun> stageSteps, PipelineStageDefinition stageDef,
        Dictionary<string, string> resolvedVars, List<string> completedStages, bool previousStageFailed,
        HashSet<string> secretKeys, int? organizationId, CancellationToken ct)
    {
        var stageVars = new Dictionary<string, string>(resolvedVars, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in stageDef.Variables)
            stageVars[key] = value;
        if (!string.IsNullOrWhiteSpace(stageDef.ExecutionRole))
            stageVars["AETHEUS_EXECUTION_ROLE"] = stageDef.ExecutionRole.Trim().ToLowerInvariant();

        // Container-isolated stage: $(WORKSPACE) is the in-container mount point, so step scripts
        // (and the checkout preamble) resolve to /w regardless of the host path the agent uses.
        var stageIsContainer = stageDef.Isolation?.IsContainer == true;
        if (stageIsContainer)
            stageVars["WORKSPACE"] = ContainerWorkspace;

        var stepsByLeg = stageSteps.GroupBy(s => s.MatrixLeg ?? string.Empty);
        var matrixLegs = ExpandMatrix(stageDef.Matrix);
        PipelineRun? releaseRun = null;
        int? releaseProjectId = null;
        // S-FEAT-16: project's default release pattern, fetched lazily once per stage (null is a valid
        // "no override" result, so a separate flag guards the single fetch).
        string? releasePattern = null;
        var releasePatternFetched = false;

        foreach (var legGroup in stepsByLeg)
        {
            var legVars = ResolveLegVariables(stageVars, legGroup.Key, matrixLegs);

            // S-FEAT-21: multi-agent build matrix. An `os` matrix axis runs this leg on an
            // OS-appropriate runner; every other leg (and all legs of a container stage) reuses the
            // stage's already isolation-checked server. Server/workspace state is therefore per-leg.
            var legServer = await ResolveMatrixLegServerAsync(stageDef, legVars, server, stageIsContainer, organizationId, ct).ConfigureAwait(false);

            // A leg with no OS-appropriate runner, or one that lands on a runner violating the stage's
            // isolation contract, fails the whole run (fail-closed). Marking the steps Failed without
            // failing the run would strand it - no task is ever dispatched to re-trigger advancement.
            if (legServer is null)
            {
                await FailRunWithUnmatchedStagesAsync(runId,
                    [$"Stage '{stageDef.Name}' matrix leg '{legGroup.Key}': no online runner available for os '{legVars.GetValueOrDefault("os")}'."],
                    legGroup.ToList(), ct).ConfigureAwait(false);
                return true;
            }
            // The stage server already passed CheckIsolationPolicy upstream; re-check here because a
            // per-leg `os` runner is resolved without that gate and could be a containers-only host.
            var legIsolationViolation = CheckIsolationPolicy(stageDef.Name, stageDef, legServer);
            if (legIsolationViolation is not null)
            {
                await FailRunWithUnmatchedStagesAsync(runId, [legIsolationViolation], legGroup.ToList(), ct).ConfigureAwait(false);
                return true;
            }

            var isWindows = OsTypeHelper.IsWindows(legServer.OsType, legServer.OsDescription);
            if (!stageIsContainer && isWindows && legVars.TryGetValue("WORKSPACE", out var ws) && !ws.Contains(":\\"))
            {
                var runIdStr = legVars.GetValueOrDefault("BUILD_BUILDID", "0");
                legVars["WORKSPACE"] = int.TryParse(runIdStr, out var parsedRunId)
                    ? PipelineCommandBuilder.GetDefaultWorkspace(parsedRunId, isWindows: true)
                    : PipelineCommandBuilder.GetDefaultWorkspace(runId, isWindows: true);
            }

            InjectStageSystemVariables(legVars, stageDef.Name, legServer);

            var targetViolation = PipelineDeploymentTargetGuard.ValidateServer(legVars, legServer);
            if (targetViolation is not null)
            {
                await FailRunWithUnmatchedStagesAsync(runId, [targetViolation], legGroup.ToList(), ct).ConfigureAwait(false);
                return true;
            }

            foreach (var stepRun in legGroup)
            {
                var baseStepName = stepRun.MatrixLeg is not null
                    ? stepRun.StepName.Replace($" [{stepRun.MatrixLeg}]", string.Empty)
                    : stepRun.StepName;

                var stepDef = stageDef.Steps.FirstOrDefault(s => s.Name == baseStepName);
                if (stepDef is null)
                {
                    // No matching YAML definition (snapshot drift / matrix-leg naming mismatch). Leaving the
                    // step run Pending would STRAND the run (it never gets a task, so the stage never completes
                    // and the run hangs). Fail it honestly with a log instead of a silent, message-less hang.
                    logger.LogWarning("Run {RunId}: step '{Step}' in stage '{Stage}' has no matching YAML definition; failing it to avoid a stranded run",
                        runId, stepRun.StepName, stageDef.Name);
                    stepRun.Status = TaskExecutionStatus.Failed;
                    stepRun.ExitCode = -1;
                    stepRun.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
                    continue;
                }

                if (!EvaluateCondition(stepDef.Condition, legVars, completedStages, previousStageFailed))
                {
                    stepRun.Status = TaskExecutionStatus.Cancelled;
                    stepRun.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
                    continue;
                }

                // Substitute step type - replace #{VAR}# tokens in target files
                if (string.Equals(stepDef.Type, "substitute", StringComparison.OrdinalIgnoreCase))
                {
                    var subVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AETHEUS_RUN_ID"] = runId.ToString(),
                        ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
                    };
                    stepRun.ServerId = legServer.Id;
                    var subTask = new ServerTask
                    {
                        ServerId = legServer.Id,
                        Name = stepRun.StepName,
                        Command = JsonSerializer.Serialize(stepDef.TargetFiles),
                        PipelineRunId = runId,
                        PipelineStepRunId = stepRun.Id,
                        EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(subVars)),
                        TimeoutSeconds = stepDef.TimeoutSeconds,
                        Operation = OperationKind.PipelineSubstituteVariables
                    };
                    repo.TrackTask(subTask);
                    MarkStepDispatched(stepRun, subTask);
                    continue;
                }

                // Release step type
                if (string.Equals(stepDef.Type, "release", StringComparison.OrdinalIgnoreCase))
                {
                    // F-034: fetch the run once per stage (not per release step), without null-forgiving.
                    if (releaseProjectId is null)
                    {
                        releaseRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
                        releaseProjectId = releaseRun?.Pipeline is null
                            ? 0
                            : await repo.GetPipelineProjectIdAsync(releaseRun.Pipeline, ct).ConfigureAwait(false) ?? 0;
                    }
                    var releaseVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AETHEUS_PROJECT_ID"] = releaseProjectId.Value.ToString(),
                        ["AETHEUS_RUN_ID"] = runId.ToString(),
                        ["AETHEUS_CHANGELOG"] = stepDef.Changelog.ToString().ToLowerInvariant(),
                        ["AETHEUS_RELEASE_DEPLOYED"] = stepDef.Deployed.ToString().ToLowerInvariant(),
                        ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
                    };
                    if (IsGitCommitHash(releaseRun?.CommitHash))
                        releaseVars["AETHEUS_RELEASE_COMMIT"] = releaseRun!.CommitHash!;
                    if (!string.IsNullOrWhiteSpace(releaseRun?.BranchName))
                        releaseVars["AETHEUS_RELEASE_BRANCH"] = releaseRun!.BranchName;

                    if (!string.IsNullOrWhiteSpace(stepDef.ArtifactSourcePipeline))
                    {
                        var (artifact, error) = await ResolveArtifactSourceAsync(
                            runId, legVars, stepDef, ct).ConfigureAwait(false);
                        if (artifact is null)
                        {
                            var now = timeProvider.GetUtcNow().UtcDateTime;
                            stepRun.ServerId = legServer.Id;
                            stepRun.Status = TaskExecutionStatus.Failed;
                            stepRun.StartedAt ??= now;
                            stepRun.CompletedAt = now;
                            await repo.AppendRunWarningsAsync(runId,
                                [$"Release step '{stepRun.StepName}': {error}"], ct).ConfigureAwait(false);
                            continue;
                        }
                        releaseVars["AETHEUS_RELEASE_ARTIFACT_RUN_ID"] = artifact.PipelineRunId.ToString();
                    }
                    // S-FEAT-16: explicit step version wins; otherwise the project's pattern, else the built-in default.
                    if (!releasePatternFetched)
                    {
                        if (releaseProjectId.Value > 0)
                            releasePattern = await repo.GetProjectReleasePatternAsync(releaseProjectId.Value, ct).ConfigureAwait(false);
                        releasePatternFetched = true;
                    }
                    var versionPattern = stepDef.Version
                        ?? (string.IsNullOrWhiteSpace(releasePattern) ? "1.0.$(BUILD_BUILDID)" : releasePattern);
                    var version = SubstituteVariables(versionPattern, legVars);
                    stepRun.ServerId = legServer.Id;
                    var releaseTask = new ServerTask
                    {
                        ServerId = legServer.Id,
                        Name = stepRun.StepName,
                        Command = version,
                        PipelineRunId = runId,
                        PipelineStepRunId = stepRun.Id,
                        EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(releaseVars)),
                        TimeoutSeconds = stepDef.TimeoutSeconds,
                        Operation = OperationKind.PipelineCreateRelease
                    };
                    repo.TrackTask(releaseTask);
                    MarkStepDispatched(stepRun, releaseTask);
                    continue;
                }

                // Coverage step type - publish Cobertura XML
                if (string.Equals(stepDef.Type, "coverage", StringComparison.OrdinalIgnoreCase))
                {
                    var covVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AETHEUS_RUN_ID"] = runId.ToString(),
                        ["AETHEUS_STAGE_NAME"] = stageDef.Name,
                        ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
                    };
                    // S-FEAT-K3P8: pass the configurable coverage threshold so the agent fails the step below it.
                    if (stepDef.MinCoverage is { } minCov)
                        covVars["AETHEUS_MIN_COVERAGE"] = minCov.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    stepRun.ServerId = legServer.Id;
                    var covTask = new ServerTask
                    {
                        ServerId = legServer.Id,
                        Name = stepRun.StepName,
                        Command = JsonSerializer.Serialize(stepDef.TargetFiles.Count > 0 ? stepDef.TargetFiles : new List<string> { "**/coverage.cobertura.xml" }),
                        PipelineRunId = runId,
                        PipelineStepRunId = stepRun.Id,
                        EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(covVars)),
                        TimeoutSeconds = stepDef.TimeoutSeconds,
                        Operation = OperationKind.PipelinePublishCoverage
                    };
                    repo.TrackTask(covTask);
                    MarkStepDispatched(stepRun, covTask);
                    continue;
                }

                // Lint step type - publish SARIF report
                if (string.Equals(stepDef.Type, "lint", StringComparison.OrdinalIgnoreCase))
                {
                    var lintVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AETHEUS_RUN_ID"] = runId.ToString(),
                        ["AETHEUS_STAGE_NAME"] = stageDef.Name,
                        ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
                    };
                    stepRun.ServerId = legServer.Id;
                    var lintTask = new ServerTask
                    {
                        ServerId = legServer.Id,
                        Name = stepRun.StepName,
                        Command = JsonSerializer.Serialize(stepDef.TargetFiles.Count > 0 ? stepDef.TargetFiles : new List<string> { "**/*.sarif" }),
                        PipelineRunId = runId,
                        PipelineStepRunId = stepRun.Id,
                        EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(lintVars)),
                        TimeoutSeconds = stepDef.TimeoutSeconds,
                        Operation = OperationKind.PipelinePublishLint
                    };
                    repo.TrackTask(lintTask);
                    MarkStepDispatched(stepRun, lintTask);
                    continue;
                }

                // Complexity step type - analyze C# cyclomatic complexity + LOC over the workspace (L)
                if (string.Equals(stepDef.Type, "complexity", StringComparison.OrdinalIgnoreCase))
                {
                    var complexityVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AETHEUS_RUN_ID"] = runId.ToString(),
                        ["AETHEUS_STAGE_NAME"] = stageDef.Name,
                        ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
                    };
                    // S-FEAT-D7M5: pass the configurable max-CC budget so the agent fails the step when exceeded.
                    if (stepDef.MaxComplexity is { } maxCc)
                        complexityVars["AETHEUS_MAX_COMPLEXITY"] = maxCc.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    stepRun.ServerId = legServer.Id;
                    var complexityTask = new ServerTask
                    {
                        ServerId = legServer.Id,
                        Name = stepRun.StepName,
                        Command = string.Empty,
                        PipelineRunId = runId,
                        PipelineStepRunId = stepRun.Id,
                        EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(complexityVars)),
                        TimeoutSeconds = stepDef.TimeoutSeconds,
                        Operation = OperationKind.PipelinePublishComplexity
                    };
                    repo.TrackTask(complexityTask);
                    MarkStepDispatched(stepRun, complexityTask);
                    continue;
                }

                // Restore an artifact produced by a verified child pipeline into this run's workspace.
                // The source run is resolved from the direct orchestration parent, never from a raw YAML id.
                if (string.Equals(stepDef.Type, "restore-artifacts", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateRestoreArtifactsTaskAsync(runId, legServer, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Destructive live DB restore. This typed step is deliberately only useful when the
                // caller has supplied a server-verified backup run (manual rollback flow); YAML never
                // carries a filesystem path or credential.
                if (string.Equals(stepDef.Type, "restore-backup", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateBackupRestoreTaskAsync(runId, legServer, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Deploy step type - apply a build artifact (or an existing release) on this server.
                if (string.Equals(stepDef.Type, "deploy", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateDeployTaskAsync(runId, legServer, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Apache reverse-proxy step type - render a vhost and apply it on the host.
                if (string.Equals(stepDef.Type, "apache-proxy", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateApacheProxyTaskAsync(runId, legServer, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Certbot HTTPS step type - obtain a cert on the host (self-signed fallback agent-side).
                if (string.Equals(stepDef.Type, "certbot", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateCertbotTaskAsync(runId, legServer, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Trigger step type - launch another pipeline (same project) and WAIT for it. No agent
                // task: the step stays Running until the child run completes (handled by
                // PipelineRunCompletedTriggerHandler), which mirrors its status and advances this stage.
                if (string.Equals(stepDef.Type, "trigger", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateTriggerStepAsync(runId, stepRun, stepDef, legVars, ct).ConfigureAwait(false);
                    continue;
                }

                // Container-isolated steps run on Linux inside the container - build the Linux script.
                var command = PipelineCommandBuilder.BuildStepCommand(stepDef, legVars, isWindows && !stageIsContainer);
                var stepEnv = ScopeStepEnvironment(legVars, secretKeys, stepDef);
                var stepEnvJson = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(stepEnv));
                stepRun.ServerId = legServer.Id;
                var task = new ServerTask
                {
                    ServerId = legServer.Id,
                    Name = stepRun.StepName,
                    Command = command,
                    PipelineRunId = runId,
                    PipelineStepRunId = stepRun.Id,
                    EnvironmentVariables = stepEnvJson,
                    TimeoutSeconds = stepDef.TimeoutSeconds
                };
                foreach (var w in ApplyContainerIsolation(task, stageDef.Isolation))
                    logger.LogWarning("Run {RunId} step '{Step}': {Warning}", runId, stepRun.StepName, w);
                repo.TrackTask(task);
                MarkStepDispatched(stepRun, task);
            }
        }

        return false; // the run was not finalized by this stage
    }

    private async Task CreateRestoreArtifactsTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        async Task FailAsync(string reason)
        {
            stepRun.ServerId = legServer.Id;
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
            await repo.AppendRunWarningsAsync(runId, [$"Restore-artifacts step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var (artifact, error) = await ResolveArtifactSourceAsync(runId, legVars, stepDef, ct).ConfigureAwait(false);
        if (artifact is null)
        {
            // A first rollback-contract release cannot have a retained V-1 artifact. Imported/tag-only
            // release metadata may also predate artifact retention. This opt-in is intentionally limited
            // by strict YAML validation to an explicit published-release selector: it tolerates only the
            // absence discovered by the control plane, never an invalid selector or a failed download.
            var releaseSelector = stepDef.Release?.Trim();
            if (stepDef.AllowMissing
                && IsBootstrapReleaseSelector(releaseSelector)
                && error?.Contains("has no retained artifact", StringComparison.Ordinal) == true)
            {
                var currentRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
                var projectId = currentRun?.Pipeline is null
                    ? null
                    : await repo.GetPipelineProjectIdAsync(currentRun.Pipeline, ct).ConfigureAwait(false);
                var hasPriorContract = projectId is not null
                    && (string.Equals(releaseSelector, "previous-published", StringComparison.OrdinalIgnoreCase)
                        ? IsGitCommitHash(currentRun?.CommitHash)
                          && await artifactRepo.HasPreviousPublishedRollbackContractReleaseAsync(
                              projectId.Value, currentRun!.CommitHash!, ct).ConfigureAwait(false)
                        : string.Equals(releaseSelector, "previous-deployed", StringComparison.OrdinalIgnoreCase)
                          ? IsGitCommitHash(currentRun?.CommitHash)
                            && await artifactRepo.HasPreviousDeployedRollbackContractReleaseAsync(
                                projectId.Value, currentRun!.CommitHash!, ct).ConfigureAwait(false)
                        : await artifactRepo.HasPublishedRollbackContractReleaseAsync(
                            projectId.Value, ct).ConfigureAwait(false));
                if (projectId is null || hasPriorContract)
                {
                    await FailAsync(error).ConfigureAwait(false);
                    return;
                }

                stepRun.ServerId = legServer.Id;
                stepRun.Status = TaskExecutionStatus.Success;
                stepRun.StartedAt ??= now;
                stepRun.CompletedAt = now;
                await repo.AppendRunWarningsAsync(runId,
                    [$"Restore-artifacts step '{stepRun.StepName}': no retained rollback-capable artifact exists; explicit bootstrap mode selected."],
                    ct).ConfigureAwait(false);
                return;
            }

            await FailAsync(error ?? "the requested artifact could not be resolved.").ConfigureAwait(false);
            return;
        }

        var targetDirectory = SubstituteVariables(stepDef.TargetDirectory ?? string.Empty, legVars).Trim();
        if (!IsSafeRelativeRestoreDirectory(targetDirectory))
        {
            await FailAsync("target_directory must be a safe relative path.").ConfigureAwait(false);
            return;
        }
        if (artifact.Sha256 is not { Length: 64 } artifactSha256
            || artifactSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            await FailAsync($"artifact '{artifact.Name}' has no valid authoritative SHA-256.").ConfigureAwait(false);
            return;
        }

        var restoreVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_RESTORE_ARTIFACT_ID"] = artifact.Id.ToString(),
            ["AETHEUS_RESTORE_RUN_ID"] = runId.ToString(),
            ["AETHEUS_RESTORE_ARTIFACT_SHA256"] = artifactSha256,
            ["AETHEUS_WORKING_DIR"] = legVars.GetValueOrDefault("WORKSPACE", "")
        };
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            restoreVars["AETHEUS_RESTORE_TARGET_DIR"] = targetDirectory;

        stepRun.ServerId = legServer.Id;
        var restoreTask = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = artifact.Name,
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(restoreVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.PipelineRestoreArtifacts
        };
        repo.TrackTask(restoreTask);
        MarkStepDispatched(stepRun, restoreTask);
    }

    private async Task<(PipelineArtifact? Artifact, string? Error)> ResolveArtifactSourceAsync(
        int runId, Dictionary<string, string> variables, PipelineStepDefinition stepDef,
        CancellationToken ct)
    {
        var releaseSelector = SubstituteVariables(stepDef.Release ?? string.Empty, variables).Trim();
        var artifactName = SubstituteVariables(stepDef.Artifact ?? string.Empty, variables).Trim();
        var sourcePipelineName = SubstituteVariables(stepDef.ArtifactSourcePipeline ?? string.Empty, variables).Trim();
        if (!string.IsNullOrWhiteSpace(releaseSelector))
        {
            if (!string.IsNullOrWhiteSpace(artifactName) || !string.IsNullOrWhiteSpace(sourcePipelineName))
                return (null, "'release' cannot be combined with 'artifact' or 'artifact_source_pipeline'.");

            var releaseRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
            var releaseProjectId = releaseRun?.Pipeline is null
                ? null
                : await repo.GetPipelineProjectIdAsync(releaseRun.Pipeline, ct).ConfigureAwait(false);
            if (releaseProjectId is null)
                return (null, "the current run is not attached to a project.");

            PipelineArtifact? releaseArtifact;
            if (string.Equals(releaseSelector, "previous-published", StringComparison.OrdinalIgnoreCase)
                || string.Equals(releaseSelector, "previous-deployed", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsGitCommitHash(releaseRun?.CommitHash))
                    return (null, "the current run has no verified source commit.");
                releaseArtifact = string.Equals(releaseSelector, "previous-deployed", StringComparison.OrdinalIgnoreCase)
                    ? await artifactRepo.FindPreviousDeployedReleaseArtifactAsync(
                        releaseProjectId.Value, releaseRun!.CommitHash!, ct).ConfigureAwait(false)
                    : await artifactRepo.FindPreviousPublishedReleaseArtifactAsync(
                        releaseProjectId.Value, releaseRun!.CommitHash!, ct).ConfigureAwait(false);
            }
            else
            {
                releaseArtifact = await artifactRepo.FindReleaseArtifactAsync(
                    releaseProjectId.Value, releaseSelector, ct).ConfigureAwait(false);
            }
            return releaseArtifact is null
                ? (null, $"release '{releaseSelector}' has no retained artifact in this project.")
                : (releaseArtifact, null);
        }

        if (string.IsNullOrWhiteSpace(artifactName) || string.IsNullOrWhiteSpace(sourcePipelineName))
            return (null, "'release' or both 'artifact' and 'artifact_source_pipeline' are required.");

        var currentRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (currentRun?.Pipeline is null || !IsGitCommitHash(currentRun.CommitHash))
            return (null, "the current run has no verified source commit.");

        var currentProjectId = await repo.GetPipelineProjectIdAsync(currentRun.Pipeline, ct).ConfigureAwait(false);
        if (currentProjectId is null)
            return (null, "the current run is not attached to a project.");

        var lineageRootRunId = runId;
        if (variables.TryGetValue("UPSTREAM_RUN_ID", out var parentRunIdText)
            && int.TryParse(parentRunIdText, out var parentRunId) && parentRunId > 0)
            lineageRootRunId = parentRunId;

        var sourceRunId = await repo.FindTriggeredRunIdByPipelineNameAsync(
            lineageRootRunId, sourcePipelineName, ct).ConfigureAwait(false);
        if (sourceRunId is { } verifiedRunId)
        {
            var sourceRun = await repo.GetPipelineRunWithPipelineAsync(verifiedRunId, ct).ConfigureAwait(false);
            var sourceProjectId = sourceRun?.Pipeline is null
                ? null
                : await repo.GetPipelineProjectIdAsync(sourceRun.Pipeline, ct).ConfigureAwait(false);
            if (sourceProjectId != currentProjectId
                || sourceRun?.Status != PipelineStatus.Success
                || !string.Equals(sourceRun.CommitHash, currentRun.CommitHash, StringComparison.OrdinalIgnoreCase))
                return (null, "the source run is not a successful same-project run at the exact source commit.");

            var childArtifact = await artifactRepo.FindRunArtifactByNameAsync(
                verifiedRunId, artifactName, ct).ConfigureAwait(false);
            return childArtifact?.ProjectId == currentProjectId
                ? (childArtifact, null)
                : (null, $"artifact '{artifactName}' was not found on successful {sourcePipelineName} run {verifiedRunId}.");
        }

        var existingArtifact = await artifactRepo.FindSuccessfulPipelineArtifactByCommitAsync(
            currentProjectId.Value, sourcePipelineName, currentRun.CommitHash!, artifactName, ct).ConfigureAwait(false);
        return existingArtifact is null
            ? (null, $"artifact '{artifactName}' was not found on a successful {sourcePipelineName} run at commit {currentRun.CommitHash}.")
            : (existingArtifact, null);
    }

    private static bool IsBootstrapReleaseSelector(string? selector) =>
        string.Equals(selector, "latest-published", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "previous-published", StringComparison.OrdinalIgnoreCase)
        || string.Equals(selector, "previous-deployed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeRelativeRestoreDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (Path.IsPathRooted(path)) return false;
        return !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private async Task CreateBackupRestoreTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        async Task FailAsync(string reason)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            stepRun.ServerId = legServer.Id;
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
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
        if (backup?.BackupPolicy is null
            || backup.ServerId != legServer.Id
            || backup.Status != BackupRunStatus.Succeeded
            || backup.RestoreCheckStatus != RestoreCheckStatus.Verified
            || string.IsNullOrWhiteSpace(backup.ArchivePath))
        {
            await FailAsync("the selected backup is not successful, verified, or owned by this target server.").ConfigureAwait(false);
            return;
        }

        // A verified archive is still not interchangeable between projects.  Resolve the project
        // from the run being executed (rather than trusting the YAML or a caller-provided id) before
        // exposing the archive and DB credentials to the agent.
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var runProjectId = run?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (runProjectId is null || runProjectId != backup.BackupPolicy.ProjectId)
        {
            await FailAsync("the selected backup belongs to a different project.").ConfigureAwait(false);
            return;
        }

        var policy = backup.BackupPolicy;
        var restoreVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            [BackupConstants.EngineEnvVar] = policy.DbEngine.ToString(),
            [BackupConstants.PolicyIdEnvVar] = policy.Id.ToString(),
            [BackupConstants.RunIdEnvVar] = backup.Id.ToString(),
            [BackupConstants.ArchivePathEnvVar] = backup.ArchivePath
        };
        if (!string.IsNullOrWhiteSpace(policy.DbHost)) restoreVars[BackupConstants.DbHostEnvVar] = policy.DbHost;
        if (policy.DbPort is not null) restoreVars[BackupConstants.DbPortEnvVar] = policy.DbPort.Value.ToString();
        if (!string.IsNullOrWhiteSpace(policy.DbName)) restoreVars[BackupConstants.DbNameEnvVar] = policy.DbName;
        if (!string.IsNullOrWhiteSpace(policy.DbUser)) restoreVars[BackupConstants.DbUserEnvVar] = policy.DbUser;
        if (!string.IsNullOrWhiteSpace(policy.DbPasswordEncrypted))
            restoreVars[BackupConstants.DbPasswordEnvVar] = encryption.DecryptValue(policy.DbPasswordEncrypted);

        stepRun.ServerId = legServer.Id;
        var restoreTask = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = backup.Id.ToString(),
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(restoreVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.BackupRestore
        };
        repo.TrackTask(restoreTask);
        MarkStepDispatched(stepRun, restoreTask);
    }

    // Cross-agent deploy task: resolve the artifact (same-run by name, or an existing release), build
    // the AETHEUS_DEPLOY_* env, and dispatch an OperationKind.PipelineDeploy to the (already
    // deploy-vetted) server. On any resolution failure the step is failed HONESTLY (status Failed +
    // a run warning) - never dispatched as a fake green.
    private async Task CreateDeployTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        async Task FailAsync(string reason)
        {
            stepRun.ServerId = legServer.Id;
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
            await repo.AppendRunWarningsAsync(runId, [$"Deploy step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var app = SubstituteVariables(stepDef.App ?? string.Empty, legVars).Trim();
        if (!OperationTargetValidator.DeployAppRegex().IsMatch(app))
        {
            await FailAsync($"invalid or missing 'app' name '{app}' (expected ^[a-zA-Z0-9_-]{{1,64}}$).").ConfigureAwait(false);
            return;
        }

        PipelineArtifact? artifact;
        int? selectedReleaseId = null;
        if (!string.IsNullOrWhiteSpace(stepDef.Artifact))
        {
            var artifactName = SubstituteVariables(stepDef.Artifact, legVars).Trim();
            artifact = await artifactRepo.FindRunArtifactByNameAsync(runId, artifactName, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(stepDef.Release))
        {
            var releaseSelector = SubstituteVariables(stepDef.Release, legVars).Trim();
            var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
            var projectId = run?.Pipeline is null ? null : await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
            var selection = projectId is { } pid
                ? await artifactRepo.FindReleaseArtifactSelectionAsync(pid, releaseSelector, ct).ConfigureAwait(false)
                : null;
            artifact = selection?.Artifact;
            selectedReleaseId = selection?.ReleaseId;
        }
        else
        {
            await FailAsync("neither 'artifact' (same-run) nor 'release' specified - nothing to deploy.").ConfigureAwait(false);
            return;
        }

        if (artifact is null)
        {
            await FailAsync("could not resolve the artifact to deploy (check the artifact name / release selector).").ConfigureAwait(false);
            return;
        }

        // Compose path ⇒ container mode (docker load + compose up); otherwise binary mode (symlink flip
        // + restart helper). The agent re-validates app/compose before any privileged action.
        var deployKind = string.IsNullOrWhiteSpace(stepDef.Compose) ? "binary" : "container";
        var deployVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_DEPLOY_ARTIFACT_ID"] = artifact.Id.ToString(),
            ["AETHEUS_DEPLOY_RUN_ID"] = runId.ToString(),
            ["AETHEUS_DEPLOY_APP"] = app,
            ["AETHEUS_DEPLOY_KIND"] = deployKind
        };
        if (selectedReleaseId is int releaseId)
            deployVars["AETHEUS_DEPLOY_RELEASE_ID"] = releaseId.ToString();
        if (!string.IsNullOrWhiteSpace(stepDef.Compose))
            deployVars["AETHEUS_DEPLOY_COMPOSE"] = SubstituteVariables(stepDef.Compose, legVars).Trim();
        // Optional dedicated health-gate window (binary mode); the agent falls back to timeout/3 when absent.
        if (stepDef.HealthTimeoutSeconds > 0)
            deployVars["AETHEUS_DEPLOY_HEALTH_TIMEOUT"] = stepDef.HealthTimeoutSeconds.ToString();
        if (!string.IsNullOrWhiteSpace(stepDef.HealthUrl))
        {
            var candidate = SubstituteVariables(stepDef.HealthUrl, legVars).Trim();
            if (!DeployHealthUrlValidator.TryNormalize(candidate, out var healthUrl))
            {
                await FailAsync("'health_url' must be an absolute loopback HTTP(S) URL without credentials or fragment.")
                    .ConfigureAwait(false);
                return;
            }
            deployVars["AETHEUS_DEPLOY_HEALTH_URL"] = healthUrl;
        }

        // PLAN-001 phase 2 zero-config OTLP: when a MonitoredApp is linked to this project, carry the OTEL
        // env under a dedicated prefix the agent writes into the app's runtime env. Best-effort and
        // OFF by default: skip entirely unless AppMonitoring:IngestBaseUrl is configured (no extra DB reads
        // on the deploy hot path), and never let a monitoring hiccup fail a real deploy (blast radius = 0).
        if (!string.IsNullOrWhiteSpace(configuration["AppMonitoring:IngestBaseUrl"]))
        {
            try
            {
                var deployRun = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
                var deployProjectId = deployRun?.Pipeline is null ? null : await repo.GetPipelineProjectIdAsync(deployRun.Pipeline, ct).ConfigureAwait(false);
                if (deployProjectId is { } appProjectId)
                {
                    var otelEnv = await appDeployEnv.GetDeployEnvAsync(appProjectId, environmentId: null, runId, ct).ConfigureAwait(false);
                    foreach (var (key, value) in otelEnv)
                        deployVars[$"AETHEUS_DEPLOY_APPENV_{key}"] = value;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OTLP deploy env injection failed for run {RunId}; deploying without it", runId);
            }
        }

        stepRun.ServerId = legServer.Id;
        var deployTask = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = app,
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(deployVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.PipelineDeploy
        };
        repo.TrackTask(deployTask);
        MarkStepDispatched(stepRun, deployTask);
    }

    // Apache reverse-proxy task: render a vhost (ServerName → Upstream), base64 it, and dispatch an
    // OperationKind.ApacheConfigureProxy to the (Apache-manage-capable) server, which writes it to
    // sites-available, enables it and gracefully reloads Apache. Invalid/missing inputs fail the step
    // HONESTLY (status Failed + run warning) - never a fake green.
    private async Task CreateApacheProxyTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        async Task FailAsync(string reason)
        {
            stepRun.ServerId = legServer.Id;
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
            await repo.AppendRunWarningsAsync(runId, [$"Apache-proxy step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var serverName = SubstituteVariables(stepDef.ServerName ?? string.Empty, legVars).Trim();
        var upstream = SubstituteVariables(stepDef.Upstream ?? string.Empty, legVars).Trim();

        if (!MailValidation.IsValidDomainName(serverName))
        {
            await FailAsync($"invalid or missing 'server_name' '{serverName}'.").ConfigureAwait(false);
            return;
        }
        if (!Uri.TryCreate(upstream, UriKind.Absolute, out var upstreamUri) ||
            (upstreamUri.Scheme != Uri.UriSchemeHttp && upstreamUri.Scheme != Uri.UriSchemeHttps))
        {
            await FailAsync($"invalid or missing 'upstream' '{upstream}' (expected http(s)://host:port).").ConfigureAwait(false);
            return;
        }

        var siteFile = $"{serverName}.conf";
        if (!OperationTargetValidator.ApacheSiteFileRegex().IsMatch(siteFile))
        {
            await FailAsync($"could not derive a safe vhost filename from '{serverName}'.").ConfigureAwait(false);
            return;
        }

        var upstreamBase = upstream.TrimEnd('/');
        var vhost =
            $$"""
            # Managed by Aetheus (pipeline run {{runId}}) - reverse proxy for {{serverName}}
            <VirtualHost *:80>
                ServerName {{serverName}}
                ProxyPreserveHost On
                ProxyRequests Off
                ProxyPass / {{upstreamBase}}/
                ProxyPassReverse / {{upstreamBase}}/
                RequestHeader set X-Forwarded-Proto "http"
                ErrorLog ${APACHE_LOG_DIR}/{{serverName}}_error.log
                CustomLog ${APACHE_LOG_DIR}/{{serverName}}_access.log combined
            </VirtualHost>

            """;

        var proxyVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_APACHE_CONFIG_B64"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(vhost)),
            ["AETHEUS_APACHE_SERVERNAME"] = serverName
        };

        stepRun.ServerId = legServer.Id;
        var task = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = siteFile,
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(proxyVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.ApacheConfigureProxy
        };
        repo.TrackTask(task);
        MarkStepDispatched(stepRun, task);
    }

    // Certbot task: dispatch an OperationKind.CertbotObtain for the requested domain(s). The agent
    // attempts a real ACME issuance and, when it cannot validate (no public DNS / unreachable :80),
    // falls back to a self-signed certificate in the Let's Encrypt layout so the site still serves
    // HTTPS ("do the closest thing"). Invalid/missing inputs fail the step honestly.
    private async Task CreateCertbotTaskAsync(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        async Task FailAsync(string reason)
        {
            stepRun.ServerId = legServer.Id;
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
            await repo.AppendRunWarningsAsync(runId, [$"Certbot step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var domainsRaw = SubstituteVariables(stepDef.Domains ?? string.Empty, legVars);
        var domains = domainsRaw
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var email = SubstituteVariables(stepDef.Email ?? string.Empty, legVars).Trim();

        if (domains.Count == 0 || domains.Any(d => !MailValidation.IsValidDomainName(d)))
        {
            await FailAsync($"invalid or missing 'domains' '{domainsRaw}'.").ConfigureAwait(false);
            return;
        }
        if (!string.IsNullOrEmpty(email) && !MailValidation.IsValidEmail(email))
        {
            await FailAsync($"invalid 'email' '{email}'.").ConfigureAwait(false);
            return;
        }

        var certVars = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_CERTBOT_DOMAINS"] = string.Join(",", domains),
            ["AETHEUS_CERTBOT_EMAIL"] = email,
            ["AETHEUS_CERTBOT_MODE"] = legVars.GetValueOrDefault("CERTBOT_MODE", PipelineDeploymentTargetGuard.Production)
        };

        stepRun.ServerId = legServer.Id;
        var task = new ServerTask
        {
            ServerId = legServer.Id,
            Name = stepRun.StepName,
            Command = domains[0],
            PipelineRunId = runId,
            PipelineStepRunId = stepRun.Id,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(certVars)),
            TimeoutSeconds = stepDef.TimeoutSeconds,
            Operation = OperationKind.CertbotObtain
        };
        repo.TrackTask(task);
        MarkStepDispatched(stepRun, task);
    }

    // Hard cap on orchestration chain depth - a backstop even when no node repeats (A→B→C→…).
    private const int MaxTriggerChainDepth = 10;

    // Trigger step: resolve the named pipeline (same project), guard against cycles, start a child run
    // forwarding UPSTREAM_* context, and leave THIS step Running while linking it to the child run id.
    // The step is NOT dispatched as an agent task - PipelineRunCompletedTriggerHandler completes it when
    // the child run finishes. Any resolution/cycle failure fails the step honestly (never a fake green).
    private async Task CreateTriggerStepAsync(
        int runId, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        async Task FailAsync(string reason)
        {
            stepRun.Status = TaskExecutionStatus.Failed;
            stepRun.StartedAt ??= now;
            stepRun.CompletedAt = now;
            await repo.AppendRunWarningsAsync(runId, [$"Trigger step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
        }

        var targetName = SubstituteVariables(stepDef.Pipeline ?? string.Empty, legVars).Trim();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            await FailAsync("missing 'pipeline' name to trigger.").ConfigureAwait(false);
            return;
        }

        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var projectId = run?.Pipeline is null ? null : await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (projectId is not { } pid)
        {
            await FailAsync("trigger is only supported for project-owned pipelines.").ConfigureAwait(false);
            return;
        }

        var target = await repo.FindPipelineByNameAndProjectAsync(targetName, pid, ct).ConfigureAwait(false);
        if (target is null)
        {
            target = await MaterializeMissingTriggerTargetAsync(run!, pid, targetName, ct).ConfigureAwait(false);
            if (target is null)
            {
                await FailAsync($"pipeline '{targetName}' not found in the project or at the immutable run revision.").ConfigureAwait(false);
                return;
            }
        }

        // Cycle guard mirrors the on_success handler: refuse if the target is already in the lineage
        // (immediate A→A or deeper A→B→A) or the chain has grown past the safety depth.
        var chain = new List<int>(ReadUpstreamChain(run!.AdditionalVariablesJson)) { run.PipelineId };
        if (chain.Contains(target.Id))
        {
            await FailAsync($"cycle refused: '{targetName}' is already in the orchestration chain.").ConfigureAwait(false);
            return;
        }
        if (chain.Count >= MaxTriggerChainDepth)
        {
            await FailAsync($"orchestration chain depth {chain.Count} reached the limit {MaxTriggerChainDepth}.").ConfigureAwait(false);
            return;
        }

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in stepDef.Variables)
        {
            if (!string.IsNullOrWhiteSpace(key))
                vars[key] = SubstituteVariables(value, legVars);
        }
        vars["UPSTREAM_RUN_ID"] = runId.ToString();
        vars["UPSTREAM_PIPELINE"] = run.Pipeline!.Name;
        vars["UPSTREAM_CHAIN"] = string.Join(",", chain);
        if (IsGitCommitHash(run.CommitHash))
            vars[SourceCommitVariable] = run.CommitHash!;
        if (!string.IsNullOrWhiteSpace(run.BranchName))
            vars[SourceBranchVariable] = run.BranchName;

        var preparation = await PrepareRunCoreAsync(target.Id, ResolveRunBranch(vars),
            yamlOverride: null, ResolveSourceCommit(vars), ct).ConfigureAwait(false);
        if (preparation is null
            || !await ChainedRunAuthorizedAsync(preparation, target.Name, target.CreatedByUsername, ct).ConfigureAwait(false))
        {
            var ownerLabel = string.IsNullOrWhiteSpace(target.CreatedByUsername) ? "(none)" : target.CreatedByUsername;
            await FailAsync($"owner '{ownerLabel}' of pipeline '{targetName}' lacks Server.Admin on its target servers.").ConfigureAwait(false);
            return;
        }

        var child = await TriggerPreparedRunAsync(preparation, vars, parameters: null, ct).ConfigureAwait(false);
        if (child is null)
        {
            await FailAsync($"could not start pipeline '{targetName}'.").ConfigureAwait(false);
            return;
        }

        // Leave the step Running (no agent task) - it now waits on the child run.
        stepRun.Status = TaskExecutionStatus.Running;
        stepRun.StartedAt ??= now;
        stepRun.TriggeredRunId = child.Id;
    }

    /// <summary>
    /// Git-strict projects may receive the parent definition before its newly-added child has been
    /// mirrored into the database (for example the first self-hosted release after adding a pipeline).
    /// Recover only from the exact immutable commit already pinned to the parent run. This is not a
    /// database-YAML fallback: an absent, malformed or differently-named Git definition still fails.
    /// </summary>
    private async Task<Pipeline?> MaterializeMissingTriggerTargetAsync(
        PipelineRun parentRun, int projectId, string targetName, CancellationToken ct)
    {
        if (!IsGitCommitHash(parentRun.CommitHash) || parentRun.Pipeline is null)
            return null;

        var yaml = await pipelineGit.ReadProjectPipelineYamlAtRevisionAsync(
            projectId, targetName, parentRun.CommitHash!, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(yaml))
            return null;

        var definition = YamlParsingHelper.ParseAndValidate(yaml, logger);
        if (definition is null
            || definition.Stages.Count == 0
            || !string.Equals(definition.Name?.Trim(), targetName, StringComparison.Ordinal))
            return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var target = new Pipeline
        {
            Name = targetName,
            Description = "Materialized from an immutable Git trigger dependency.",
            YamlDefinition = yaml,
            TriggerType = definition.Trigger.Equals("webhook", StringComparison.OrdinalIgnoreCase)
                ? PipelineTriggerType.Webhook
                : definition.Trigger.Equals("schedule", StringComparison.OrdinalIgnoreCase)
                    ? PipelineTriggerType.Schedule
                    : PipelineTriggerType.Manual,
            ProjectId = projectId,
            SourceBranch = parentRun.BranchName,
            CreatedByUsername = parentRun.Pipeline.CreatedByUsername,
            CreatedAt = now,
            UpdatedAt = now
        };
        await repo.AddPipelineAsync(target, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Materialized missing trigger pipeline '{Pipeline}' for project {ProjectId} from immutable commit {Commit}.",
            targetName, projectId, parentRun.CommitHash);
        return target;
    }

    internal static bool IsGitCommitHash(string? value)
        => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    private static string? ResolveSourceCommit(IReadOnlyDictionary<string, string>? additionalVariables)
        => additionalVariables is not null
            && additionalVariables.TryGetValue(SourceCommitVariable, out var commit)
            && IsGitCommitHash(commit)
                ? commit
                : null;

    private static string? ResolveRunBranch(IReadOnlyDictionary<string, string>? additionalVariables)
    {
        if (additionalVariables is not null
            && additionalVariables.TryGetValue(SourceBranchVariable, out var selectedBranch)
            && !string.IsNullOrWhiteSpace(selectedBranch))
            return selectedBranch;

        if (additionalVariables is null
            || !additionalVariables.TryGetValue("WEBHOOK_REF", out var reference)
            || !reference.StartsWith("refs/heads/", StringComparison.Ordinal)) return null;

        return reference["refs/heads/".Length..];
    }

    // F-EXEC-1b (chaining): re-authorize the CHILD pipeline's owner against the child's candidate target
    // servers before a chained run starts. Mirrors TriggerAutomatedRunAsync's owner check - the upstream
    // caller may administer one pipeline's servers but not the next hop's, so without this a trigger/
    // on_success hop could execute free-form shell (RCE) outside the caller's authority. Fail closed.
    private async Task<bool> ChainedRunAuthorizedAsync(
        PipelineRunPreparation preparation, string childPipelineName, string? owner, CancellationToken ct)
    {
        var childPipelineId = preparation.PipelineId;
        if (string.IsNullOrEmpty(owner))
        {
            logger.LogWarning(
                "Chained run of pipeline {PipelineId} blocked (F-EXEC-1b): pipeline has no owner.", childPipelineId);
            await audit.LogAsync("BlockedUnownedChainedRun", "Pipeline", childPipelineId, childPipelineName, ct).ConfigureAwait(false);
            return false;
        }

        var targetIds = preparation.TargetServerIds;
        foreach (var serverId in targetIds)
        {
            if (!await authz.HasPermissionAsync(owner, ResourceType.Server, serverId, Permission.Admin, ct).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "Chained run of pipeline {PipelineId} blocked (F-EXEC-1b): owner '{Owner}' lacks " +
                    "Server.Admin on target server {ServerId}.", childPipelineId, owner, serverId);
                await audit.LogAsync("BlockedUnauthorizedChainedRun", "Pipeline", childPipelineId,
                    $"{childPipelineName} | Servers: {string.Join(',', targetIds)}", ct).ConfigureAwait(false);
                return false;
            }
        }
        return true;
    }

    public async Task<PipelineRunDto?> TriggerChainedRunAsync(
        int childPipelineId, Dictionary<string, string> upstreamVariables, CancellationToken ct = default)
    {
        var child = await repo.FindPipelineAsync(childPipelineId, ct).ConfigureAwait(false);
        if (child is null) return null;
        var preparation = await PrepareRunCoreAsync(child.Id, ResolveRunBranch(upstreamVariables),
            yamlOverride: null, ResolveSourceCommit(upstreamVariables), ct).ConfigureAwait(false);
        if (preparation is null
            || !await ChainedRunAuthorizedAsync(preparation, child.Name, child.CreatedByUsername, ct).ConfigureAwait(false))
            return null;
        return await TriggerPreparedRunAsync(preparation, upstreamVariables, parameters: null, ct).ConfigureAwait(false);
    }

    // Parse the comma-separated ancestor pipeline ids carried in UPSTREAM_CHAIN (run.AdditionalVariablesJson).
    private static IReadOnlyList<int> ReadUpstreamChain(string? additionalVariablesJson)
    {
        if (string.IsNullOrWhiteSpace(additionalVariablesJson)) return [];
        try
        {
            var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(additionalVariablesJson);
            if (vars is null || !vars.TryGetValue("UPSTREAM_CHAIN", out var chainStr) || string.IsNullOrWhiteSpace(chainStr))
                return [];
            return chainStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue).Select(id => id!.Value).ToList();
        }
        catch (JsonException) { return []; }
    }

    private async Task<bool> CreateSystemTasksAsync(
        int runId, List<PipelineStepRun> systemSteps, Dictionary<string, string> resolvedVars,
        PipelineYamlDefinition definition, int? organizationId, List<string> completedStages, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return false;

        var hasRepo = !string.IsNullOrEmpty(run.Pipeline.Project?.RepositoryUrl);

        // Use run affinity if available - cleanup should run on the same server as prepare
        var affinityServerId = await repo.GetRunAffinityServerIdAsync(runId, ct).ConfigureAwait(false);
        var isCleanup = systemSteps.All(step => step.StageName == SystemCleanupStage);
        Server? server = null;
        if (affinityServerId is not null)
            server = await repo.FindOnlineServerByIdAsync(affinityServerId.Value, ct: ct).ConfigureAwait(false);
        if (isCleanup && affinityServerId is not null && server is null)
        {
            logger.LogWarning(
                "Run {RunId} cleanup remains undispatched because its affinity runner {ServerId} is offline; " +
                "refusing to report success from a different runner.", runId, affinityServerId.Value);
            return false;
        }
        if (server is null)
        {
            var firstStageDef = YamlParsingHelper.FlattenJobs(definition).FirstOrDefault();
            server = firstStageDef is not null
                ? await ResolveServerAsync(firstStageDef, organizationId, ct).ConfigureAwait(false)
                : await repo.FindAnyOnlineRunnerAsync(organizationId, ct: ct).ConfigureAwait(false);
        }
        if (server is null) return false;

        var isWindows = OsTypeHelper.IsWindows(server.OsType, server.OsDescription);
        // Phase 2: the whole run executes in containers when isolation is set at the run level.
        // Container execution is Linux-only - the system scripts use the Linux variants and the
        // workspace is the in-container mount point (/w); the agent bind-mounts the run's host dir.
        var runIsolation = definition.Isolation;
        var isContainer = runIsolation?.IsContainer == true;
        var useWindows = isWindows && !isContainer;

        // Fail-closed: never dispatch a container-isolated system task (prepare/clone/cleanup) to a
        // runner that can't honor the run-level isolation contract. Without this the run would hard-
        // fail late at the agent instead of being blocked cleanly here with a readable reason.
        var isolationViolation = CheckRunIsolationPolicy(server, runIsolation);
        if (isolationViolation is not null)
        {
            await FailRunWithUnmatchedStagesAsync(runId, [isolationViolation], systemSteps, ct).ConfigureAwait(false);
            return false;
        }

        foreach (var step in systemSteps)
        {
            var isPrepare = step.StageName == SystemPrepareStage;
            step.ServerId = server.Id;

            string workspace;
            if (isContainer)
                workspace = ContainerWorkspace;
            else
            {
                workspace = resolvedVars.GetValueOrDefault("WORKSPACE", "");
                if (isWindows || string.IsNullOrEmpty(workspace))
                    workspace = PipelineCommandBuilder.GetDefaultWorkspace(runId, isWindows);
            }
            resolvedVars["WORKSPACE"] = workspace;

            string command;
            if (isPrepare && hasRepo)
                command = useWindows ? PipelineCommandBuilder.BuildWindowsCloneCommand(workspace) : PipelineCommandBuilder.BuildLinuxCloneCommand(workspace);
            else if (isPrepare)
                command = useWindows ? PipelineCommandBuilder.BuildWindowsPrepareCommand(workspace) : PipelineCommandBuilder.BuildLinuxPrepareCommand(workspace);
            else
                command = useWindows ? PipelineCommandBuilder.BuildWindowsCleanupCommand(workspace) : PipelineCommandBuilder.BuildLinuxCleanupCommand(workspace);

            // The clone step authenticates to the smart-HTTP mirror with a stateless, run-scoped,
            // self-expiring token (GIT_USERNAME/GIT_PASSWORD) - minted here, never persisted. Only the
            // checkout task carries it; the env blob is encrypted end-to-end via TaskEnvProtection.
            var taskVars = resolvedVars;
            if (isPrepare && hasRepo)
            {
                var (gitUser, gitPass) = GitRunCloneToken.Mint(
                    configuration, runId, run.Pipeline.Project!.Id,
                    timeProvider.GetUtcNow().UtcDateTime, GitRunCloneToken.DefaultTtl);
                taskVars = new Dictionary<string, string>(resolvedVars, StringComparer.OrdinalIgnoreCase)
                {
                    ["GIT_USERNAME"] = gitUser,
                    ["GIT_PASSWORD"] = gitPass
                };
                // Defense in depth: the clone preamble never echoes the token, but register it so any
                // accidental leak into a log line is redacted like a vault secret.
                secretMasking.RegisterRuntimeSecret(runId, gitPass);
            }

            var envJson = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(taskVars));
            var task = new ServerTask
            {
                ServerId = server.Id,
                Name = step.StepName,
                Command = command,
                PipelineRunId = runId,
                PipelineStepRunId = step.Id,
                EnvironmentVariables = envJson,
                TimeoutSeconds = isPrepare ? PipelineCommandBuilder.PrepareTimeoutSeconds : PipelineCommandBuilder.CleanupTimeoutSeconds
            };
            foreach (var w in ApplyContainerIsolation(task, runIsolation))
                logger.LogWarning("Run {RunId} step '{Step}': {Warning}", runId, step.StepName, w);
            repo.TrackTask(task);
            MarkStepDispatched(step, task);
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // In-container mount point for the run workspace. The agent bind-mounts the run's host directory
    // here, so every container-isolated step sees the cloned source at the same path regardless of
    // where the agent stores it on the host.
    internal const string ContainerWorkspace = "/w";

    private async Task<bool> DispatchCleanupIfPendingAsync(int runId, CancellationToken ct)
    {
        var pendingCleanup = await repo.GetPendingStepRunsAsync(runId, ct).ConfigureAwait(false);
        var cleanupSteps = pendingCleanup.Where(s => s.StageName == SystemCleanupStage).ToList();
        if (cleanupSteps.Count == 0) return false;

        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return false;

        var definition = ParseRunDefinition(run);
        if (definition is null) return false;

        var resolvedVars = await ResolveFullVariablesForRunAsync(run, definition, ct).ConfigureAwait(false);
        if (await CreateSystemTasksAsync(runId, cleanupSteps, resolvedVars, definition, null, [], ct).ConfigureAwait(false))
            return true;

        if (!await IsRunActiveAsync(runId, ct).ConfigureAwait(false)) return false;
        MarkStepsAs(cleanupSteps, TaskExecutionStatus.Failed);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await repo.AppendRunWarningsAsync(runId,
            ["System cleanup could not run on the original affinity runner; its workspace requires reconciliation."],
            ct).ConfigureAwait(false);
        return false;
    }

    // S-FEAT-21: pick the runner for a matrix leg. An `os` matrix axis targets an OS-appropriate
    // agent so a single definition fans out across platforms (cross-OS build matrix). Every other
    // case reuses the stage's already isolation-checked server, so behavior is unchanged without an
    // os axis. The leg's os stays within the stage's selector + organization, so it never widens the
    // F-EXEC-1 candidate set the pre-run Server.Admin gate authorized.
    private async Task<Server?> ResolveMatrixLegServerAsync(
        PipelineStageDefinition stageDef, Dictionary<string, string> legVars, Server stageServer,
        bool stageIsContainer, int? organizationId, CancellationToken ct)
    {
        // Container stages run every leg on the single Docker-capable runner already vetted upstream.
        if (stageIsContainer) return stageServer;
        if (!legVars.TryGetValue("os", out var legOs) || string.IsNullOrWhiteSpace(legOs))
            return stageServer;

        var requested = OsTypeHelper.Parse(legOs);
        if (requested == OsType.Unknown) return stageServer;

        // No extra lookup when the leg's OS already matches the stage's resolved runner.
        var stageIsWindows = OsTypeHelper.IsWindows(stageServer.OsType, stageServer.OsDescription);
        if (stageIsWindows == (requested == OsType.Windows)) return stageServer;

        return await ResolveServerAsync(stageDef with { Os = legOs }, organizationId, ct).ConfigureAwait(false);
    }

    private async Task InjectOutputVariablesAsync(int runId, Dictionary<string, string> vars, CancellationToken ct)
    {
        var stepOutputs = await repo.GetSuccessfulStepOutputsAsync(runId, ct).ConfigureAwait(false);

        foreach (var step in stepOutputs)
        {
            var outputVars = DeserializeResolvedVariables(step.OutputVariablesJson);
            foreach (var (key, value) in outputVars)
            {
                vars[$"{step.StageName}.{step.StepName}.{key}"] = value;
                if (!key.Equals(PipelineDeploymentTargetGuard.TargetVariable, StringComparison.OrdinalIgnoreCase))
                    vars[key] = value;
            }
        }
    }

    // --- Variable resolution (delegated to the injected IPipelineVariableResolver) ---
    // F-009: thin wrappers keeping call sites terse while the logic lives in its own type.

    private Task<(Dictionary<string, string> Resolved, List<string> Warnings, HashSet<string> SecretKeys)> ResolveVariablesWithWarningsAsync(
        PipelineYamlDefinition definition, int? projectId, Dictionary<string, string>? additionalVariables, CancellationToken ct,
        int? pipelineId = null, int? runId = null, string? pipelineName = null)
        => variableResolver.ResolveVariablesWithWarningsAsync(definition, projectId, additionalVariables, ct, pipelineId, runId, pipelineName);

    private static void InjectStageSystemVariables(
        Dictionary<string, string> stageVars, string stageName, Server server)
        => PipelineVariableResolver.InjectStageSystemVariables(stageVars, stageName, server);

    private Task<Dictionary<string, string>> ResolveFullVariablesForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
        => variableResolver.ResolveFullVariablesForRunAsync(run, definition, ct);

    private Task<(Dictionary<string, string> Resolved, HashSet<string> SecretKeys)> ResolveFullVariablesWithSecretsForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
        => variableResolver.ResolveFullVariablesWithSecretsForRunAsync(run, definition, ct);

    internal static Dictionary<string, string> DeserializeResolvedVariablesStatic(string? json)
        => DeserializeResolvedVariables(json);

    // --- Run-definition parsing + IPipelineRunService context lookups ---
    /// <summary>
    /// F-012 (ADR-015): a run executes the YAML captured at trigger time - never the live
    /// definition, which may be edited mid-run. Legacy runs without a snapshot fall back to the
    /// pipeline's current definition.
    /// </summary>
    private PipelineYamlDefinition? ParseRunDefinition(PipelineRun run)
    {
        ArgumentNullException.ThrowIfNull(run.Pipeline, nameof(run.Pipeline));
        var yaml = string.IsNullOrWhiteSpace(run.YamlSnapshot) ? run.Pipeline.YamlDefinition : run.YamlSnapshot;
        return YamlParsingHelper.ParseAndValidate(yaml, logger);
    }

    public Task<int?> GetPipelineIdForRunAsync(int runId, CancellationToken ct = default)
        => repo.GetPipelineIdForRunAsync(runId, ct);

    public async Task<(int PipelineId, int? ProjectId)?> GetRunPipelineContextAsync(int runId, CancellationToken ct = default)
    {
        var dto = await GetRunAsync(runId, ct).ConfigureAwait(false);
        if (dto is null) return null;
        return (dto.PipelineId, dto.ProjectId);
    }

    public Task<int?> GetPipelineIdForApprovalAsync(int approvalId, CancellationToken ct = default)
        => repo.GetPipelineIdForApprovalAsync(approvalId, ct);

    public Task<bool> IsServerAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default)
        => repo.IsServerAssignedToRunAsync(runId, serverId, ct);
}
