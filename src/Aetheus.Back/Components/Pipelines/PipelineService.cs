// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Helpers;
using Microsoft.AspNetCore.Http;

namespace Aetheus.Back.Components.Pipelines;

public class PipelineService(
    IPipelineRepository repo,
    IPipelineGitService pipelineGit,
    IAuditService audit,
    ILogger<PipelineService> logger,
    IEntityChangeNotifier notifier,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    IPipelineTemplateResolver templateResolver,
    IOrganizationRepository organizationRepository) : IPipelineService
{
    private IPipelineTemplateResolver TemplateResolver => templateResolver;
    public async Task<PaginatedResult<PipelineDto>> GetPipelinesAsync(PipelinePaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetPipelinesPagedProjectedAsync(
            request.Search, request.TriggerType, request.EnvironmentId, request.ProjectServerId, request.ProjectId,
            page, pageSize, accessibleIds, ct).ConfigureAwait(false);

        return new PaginatedResult<PipelineDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PipelineDto?> GetPipelineAsync(int id, CancellationToken ct = default)
    {
        var pipeline = await repo.GetPipelineWithRunsAsync(id, ct).ConfigureAwait(false);
        return pipeline is null ? null : MapToDto(pipeline);
    }

    public Task<PipelineSourceDto?> GetPipelineSourceAsync(int projectId, string pipelineName, CancellationToken ct = default, string? sourceBranch = null)
        => pipelineGit.GetPipelineSourceAsync(projectId, pipelineName, ct, sourceBranch);

    public async Task<PipelineDependencyGroupsDto> GetDependencyGroupsAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var pipelines = await repo.GetPipelinesForDependencyGraphAsync(accessibleIds, ct).ConfigureAwait(false);
        return PipelineDependencyGraphBuilder.Build(pipelines, ValidateYaml);
    }

    public async Task<PaginatedResult<PipelineDependencyDto>> GetDependencyPageAsync(
        PipelinePaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, identities, totalCount) = await repo.GetPipelineDependencyPageAsync(request, accessibleIds, ct).ConfigureAwait(false);
        return new PaginatedResult<PipelineDependencyDto>
        {
            Items = PipelineDependencyGraphBuilder.BuildItems(items, identities, ValidateYaml),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PipelineDto> CreatePipelineAsync(CreatePipelineRequest request, CancellationToken ct = default)
    {
        var pipeline = new Pipeline
        {
            Name = request.Name,
            Description = request.Description,
            YamlDefinition = request.YamlDefinition,
            TriggerType = ParseTriggerType(request.YamlDefinition),
            ProjectId = request.ProjectId,
            SourceBranch = request.SourceBranch?.Trim(),
            EnvironmentId = request.EnvironmentId,
            ProjectServerId = request.ProjectServerId,
            // F-EXEC-1b: record the creator so webhook/scheduler runs can be authorized
            // against a principal (interactive runs authorize the live caller instead).
            CreatedByUsername = CurrentUsername()
        };
        // Git-first: commit to the project's internal repo BEFORE persisting the DB mirror, so a git
        // failure aborts the create instead of leaving a DB-only definition that diverges from git.
        await PipelineAuthoritativeGitCoordinator.CommitCreateAsync(
            pipeline, pipelineGit, logger, CurrentUsername() ?? "system", ct).ConfigureAwait(false);
        await repo.AddPipelineAsync(pipeline, ct).ConfigureAwait(false);
        await PipelineAuthoritativeGitCoordinator.CopyToLinkedEnvironmentProjectAsync(
            pipeline, repo, pipelineGit, logger, CurrentUsername() ?? "system", ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "Pipeline", pipeline.Id, pipeline.Name, ct).ConfigureAwait(false);
        await BroadcastPipelineChangeAsync(pipeline, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return MapToDto(pipeline);
    }

    public async Task<PipelineDto?> UpdatePipelineAsync(int id, UpdatePipelineRequest request, CancellationToken ct = default)
    {
        var pipeline = await repo.FindPipelineAsync(id, ct).ConfigureAwait(false);
        if (pipeline is null) return null;

        var snapshot = PipelineStateSnapshot.Capture(pipeline);
        var previousLocation = snapshot.ProjectId is { } oldProjectId
            ? new PipelineGitDefinitionLocation(oldProjectId, snapshot.Name, snapshot.SourceBranch)
            : null;
        var nextBranch = request.SourceBranch?.Trim();
        var nextLocation = request.ProjectId is { } newProjectId
            ? new PipelineGitDefinitionLocation(newProjectId, request.Name, nextBranch)
            : null;
        var actor = CurrentUsername() ?? "system";

        // Git is authoritative. Rename/move and content replacement happen before the DB mirror and
        // use one commit when source and destination share a repo/branch.
        var (gitOutcome, gitError) = await pipelineGit.ApplyProjectPipelineChangeAsync(
            previousLocation, nextLocation, request.YamlDefinition, actor, ct).ConfigureAwait(false);
        if (gitOutcome == GitWriteOutcome.Failed)
            throw new ConflictException(
                $"Pipeline definition could not be updated in the project's git repository: {gitError}");

        pipeline.Name = request.Name;
        pipeline.Description = request.Description;
        pipeline.YamlDefinition = request.YamlDefinition;
        pipeline.TriggerType = ParseTriggerType(request.YamlDefinition);
        pipeline.ProjectId = request.ProjectId;
        pipeline.SourceBranch = nextBranch;
        pipeline.EnvironmentId = request.EnvironmentId;
        pipeline.ProjectServerId = request.ProjectServerId;
        pipeline.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        // F-EXEC-1b: adopt ownership when a legacy/unowned pipeline is re-saved by an
        // authorized user - this is the documented remediation to re-enable its automated
        // (webhook/scheduler) triggers. Existing ownership is never silently reassigned.
        if (string.IsNullOrEmpty(pipeline.CreatedByUsername))
            pipeline.CreatedByUsername = CurrentUsername();

        try
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            snapshot.Restore(pipeline);
            if (gitOutcome == GitWriteOutcome.Committed)
            {
                var (rollbackOutcome, rollbackError) = await pipelineGit.ApplyProjectPipelineChangeAsync(
                    nextLocation, previousLocation, snapshot.YamlDefinition, actor, ct).ConfigureAwait(false);
                if (rollbackOutcome != GitWriteOutcome.Committed)
                    throw new ConflictException(
                        $"Database update failed and the authoritative Git change could not be rolled back: {rollbackError}");
            }
            throw;
        }
        await PipelineAuthoritativeGitCoordinator.CopyToLinkedEnvironmentProjectAsync(
            pipeline, repo, pipelineGit, logger, CurrentUsername() ?? "system", ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "Pipeline", pipeline.Id, pipeline.Name, ct).ConfigureAwait(false);
        await BroadcastPipelineChangeAsync(pipeline, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return MapToDto(pipeline);
    }

    public async Task<bool> DeletePipelineAsync(int id, CancellationToken ct = default)
    {
        var pipeline = await repo.FindPipelineAsync(id, ct).ConfigureAwait(false);
        if (pipeline is null) return false;
        var name = pipeline.Name;
        var location = pipeline.ProjectId is { } projectId
            ? new PipelineGitDefinitionLocation(projectId, pipeline.Name, pipeline.SourceBranch)
            : null;
        var actor = CurrentUsername() ?? "system";
        var (gitOutcome, gitError) = await pipelineGit.ApplyProjectPipelineChangeAsync(
            location, null, null, actor, ct).ConfigureAwait(false);
        if (gitOutcome == GitWriteOutcome.Failed)
            throw new ConflictException(
                $"Pipeline definition could not be deleted from the project's git repository: {gitError}");
        try
        {
            await repo.RemovePipelineAsync(pipeline, ct).ConfigureAwait(false);
        }
        catch
        {
            if (gitOutcome == GitWriteOutcome.Committed)
            {
                var (rollbackOutcome, rollbackError) = await pipelineGit.ApplyProjectPipelineChangeAsync(
                    null, location, pipeline.YamlDefinition, actor, ct).ConfigureAwait(false);
                if (rollbackOutcome != GitWriteOutcome.Committed)
                    throw new ConflictException(
                        $"Database delete failed and the authoritative Git deletion could not be rolled back: {rollbackError}");
            }
            throw;
        }
        await audit.LogAsync("Deleted", "Pipeline", id, name, ct).ConfigureAwait(false);
        await BroadcastPipelineChangeAsync(pipeline, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }


    public PipelineYamlDefinition? ValidateYaml(string yaml) =>
        YamlParsingHelper.ParseAndValidate(yaml, logger);

    // P-23: Strict YAML validation with warnings for unknown properties
    public YamlValidationResultDto ValidateYamlStrict(string yaml)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        PipelineYamlDefinition? definition;
        try
        {
            definition = YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return new YamlValidationResultDto
            {
                IsValid = false,
                Errors = [$"YAML syntax error: {ex.Message}"]
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return new YamlValidationResultDto
            {
                IsValid = false,
                Errors = [$"Parsing error: {ex.Message}"]
            };
        }

        if (definition is null)
        {
            return new YamlValidationResultDto { IsValid = false, Errors = ["Empty YAML definition."] };
        }

        // Semantic validation
        if (definition.VariableLibraries.Any(n => string.IsNullOrWhiteSpace(n)))
            errors.Add("Variable library names cannot be empty.");

        if (definition.Vaults.Any(n => string.IsNullOrWhiteSpace(n)))
            errors.Add("Vault names cannot be empty.");

        if (definition.Branches.Any(string.IsNullOrWhiteSpace))
            errors.Add("Branch filter entries cannot be empty.");

        if (!string.IsNullOrWhiteSpace(definition.SourceBranch)
            && !PipelineBranchValidator.IsValid(definition.SourceBranch))
            errors.Add("The configured source_branch is invalid.");

        // The branch filter only gates the webhook trigger; on any other trigger it is silently ignored.
        if (definition.Branches.Count > 0 && !string.Equals(definition.Trigger, "webhook", StringComparison.OrdinalIgnoreCase))
            warnings.Add("`branches:` filter only applies to the webhook trigger; it will be ignored for this trigger type.");

        if (definition.Stages.Count == 0)
            errors.Add("Pipeline must have at least one stage.");

        foreach (var stage in definition.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name))
                errors.Add("Stage name cannot be empty.");

            if (OsTypeHelper.IsUnrecognized(stage.Os))
                warnings.Add($"Stage '{stage.Name}' has an unrecognized os '{stage.Os}' - expected 'linux' or 'windows'; it will be ignored (no OS constraint).");

            if (stage.Jobs.Count > 0)
            {
                // New format: stages > jobs > steps
                foreach (var job in stage.Jobs)
                {
                    if (string.IsNullOrWhiteSpace(job.Name))
                        errors.Add($"Job name cannot be empty in stage '{stage.Name}'.");
                    if (string.IsNullOrWhiteSpace(job.Agent) && string.IsNullOrWhiteSpace(job.Pool) && string.IsNullOrWhiteSpace(job.Environment)
                        && string.IsNullOrWhiteSpace(stage.Agent) && string.IsNullOrWhiteSpace(stage.Pool) && string.IsNullOrWhiteSpace(stage.Environment))
                        warnings.Add($"Job '{job.Name}' in stage '{stage.Name}' has no agent/pool/environment - will use run affinity.");
                    if (OsTypeHelper.IsUnrecognized(job.Os))
                        warnings.Add($"Job '{job.Name}' in stage '{stage.Name}' has an unrecognized os '{job.Os}' - expected 'linux' or 'windows'; it will be ignored (no OS constraint).");
                    if (job.Steps.Count == 0)
                        errors.Add($"Job '{job.Name}' in stage '{stage.Name}' must have at least one step.");
                    ValidateSteps(errors, job.Steps, $"stage '{stage.Name}' / job '{job.Name}'");
                }
            }
            else
            {
                // Legacy format: stages > steps
                if (string.IsNullOrWhiteSpace(stage.Agent) && string.IsNullOrWhiteSpace(stage.Pool) && string.IsNullOrWhiteSpace(stage.Environment))
                    warnings.Add($"Stage '{stage.Name}' has no agent/pool/environment - will use run affinity.");
                if (stage.Steps.Count == 0)
                    errors.Add($"Stage '{stage.Name}' must have at least one step.");
                ValidateSteps(errors, stage.Steps, $"stage '{stage.Name}'");
            }
        }

        // S-UX-MXVL: reject shell-shaped matrix values at SAVE/validate time too (same check the run-creation
        // path applies), so the author sees the error in the editor immediately instead of only at first run.
        var effectiveStages = YamlParsingHelper.FlattenJobs(definition);
        errors.AddRange(PipelineRunHelpers.ValidateMatrixValues(effectiveStages));
        errors.AddRange(PipelineRunHelpers.ValidateIsolationLimits(effectiveStages));
        errors.AddRange(PipelineRunHelpers.ValidateExecutionRoles(effectiveStages));

        PipelineYamlDiagnostics.AppendUnknownPropertyWarnings(yaml, warnings, logger);

        return new YamlValidationResultDto
        {
            IsValid = errors.Count == 0,
            Definition = errors.Count == 0 ? definition : null,
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<YamlValidationResultDto> ValidateYamlStrictAsync(
        string yaml, int? projectId, int? environmentId, int? projectServerId,
        int? organizationId = null, CancellationToken ct = default)
    {
        var resolvedOrganizationId = organizationId
            ?? await repo.GetPipelineOwnerOrganizationIdAsync(projectId, environmentId, projectServerId, ct)
                .ConfigureAwait(false)
            ?? await organizationRepository.GetDefaultOrganizationIdAsync(ct).ConfigureAwait(false);
        if (resolvedOrganizationId is null)
            return new YamlValidationResultDto
            {
                IsValid = false,
                Errors = ["No organization is available for template resolution."]
            };

        try
        {
            var resolution = await TemplateResolver.ResolveAsync(
                yaml, resolvedOrganizationId.Value, new Dictionary<string, string>(), ct).ConfigureAwait(false);
            var structural = ValidateYamlStrict(resolution.Yaml);
            var sourceWarnings = ValidateYamlStrict(yaml).Warnings;
            var warnings = structural.Warnings.Concat(sourceWarnings).Distinct(StringComparer.Ordinal).ToList();
            if (resolution.UsesLegacyReference)
                warnings.Add($"Template reference '{resolution.TemplateName}' is unpinned; use @{resolution.ResolvedVersion}.");
            return structural with
            {
                Definition = structural.IsValid ? resolution.Definition : null,
                Warnings = warnings
            };
        }
        catch (BadRequestException exception)
        {
            return new YamlValidationResultDto { IsValid = false, Errors = [exception.Message] };
        }
    }

    // Step `type:` values that do real work without a `shell` command (handled by dedicated dispatch
    // branches in PipelineRunService). Keep in sync with those branches.
    private static readonly HashSet<string> KnownTypedStepTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "release", "substitute", "coverage", "complexity", "lint", "artifacts",
        "deploy", "apache-proxy", "certbot", "trigger", "restore-artifacts", "restore-backup"
    };

    private static void ValidateSteps(List<string> errors, List<PipelineStepDefinition> steps, string context)
    {
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"Step name cannot be empty in {context}.");
            // Typed steps carry their work in fields other than `shell` (no shell command required).
            var isTypedStep = !string.IsNullOrWhiteSpace(step.Type)
                && KnownTypedStepTypes.Contains(step.Type);
            if (string.IsNullOrWhiteSpace(step.Shell) && !step.Checkout && !isTypedStep)
                errors.Add($"Step '{step.Name}' in {context} must have a shell command or checkout enabled.");
            if (step.RetryCount < 0)
                errors.Add($"Step '{step.Name}' retry_count cannot be negative.");
            if (string.Equals(step.Type, "restore-artifacts", StringComparison.OrdinalIgnoreCase))
            {
                var hasUpstreamSource = !string.IsNullOrWhiteSpace(step.Artifact)
                    || !string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline);
                var hasReleaseSource = !string.IsNullOrWhiteSpace(step.Release);
                if (hasReleaseSource && hasUpstreamSource)
                    errors.Add($"Step '{step.Name}' in {context} must select either 'release' or the 'artifact'/'artifact_source_pipeline' pair, not both.");
                else if (!hasReleaseSource
                         && (string.IsNullOrWhiteSpace(step.Artifact)
                             || string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline)))
                    errors.Add($"Step '{step.Name}' in {context} requires 'release' or both 'artifact' and 'artifact_source_pipeline'.");
                if (!IsSafeRelativeDirectory(step.TargetDirectory))
                    errors.Add($"Step '{step.Name}' in {context} target_directory must be a safe relative path.");
                if (step.AllowMissing
                    && !string.Equals(step.Release?.Trim(), "latest-published", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(step.Release?.Trim(), "previous-published", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(step.Release?.Trim(), "previous-deployed", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Step '{step.Name}' in {context} allow_missing is only valid with release: latest-published, previous-published, or previous-deployed.");
            }
        }
    }

    private static bool IsSafeRelativeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (Path.IsPathRooted(path)) return false;
        return !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private PipelineTriggerType ParseTriggerType(string yaml)
    {
        var def = ValidateYaml(yaml);
        return def?.Trigger?.ToLowerInvariant() switch
        {
            "webhook" => PipelineTriggerType.Webhook,
            "schedule" => PipelineTriggerType.Schedule,
            _ => PipelineTriggerType.Manual
        };
    }

    public async Task<List<PipelineDto>> GetWebhookTriggeredPipelinesForProjectAsync(int projectId, CancellationToken ct = default)
    {
        var pipelines = await repo.GetWebhookTriggeredPipelinesForProjectAsync(projectId, ct).ConfigureAwait(false);
        return pipelines.Select(p => new PipelineDto
        {
            Id = p.Id,
            Name = p.Name,
            TriggerType = p.TriggerType,
            ProjectId = p.ProjectId,
            SourceBranch = p.SourceBranch,
            YamlDefinition = p.YamlDefinition,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToList();
    }

    public Task<bool> HasActiveRunAsync(int pipelineId, CancellationToken ct = default)
        => repo.HasActiveRunAsync(pipelineId, ct);

    public async Task<PipelineDto> UpsertPipelineFromYamlAsync(
        string name, string yamlContent, int projectId, string triggerType,
        CancellationToken ct = default, string? sourceBranch = null, string? defaultBranch = null)
    {
        var configuredSourceBranch = ValidateYaml(yamlContent)?.SourceBranch?.Trim();
        var existing = await repo.FindPipelineByNameAndProjectAsync(name, projectId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(sourceBranch))
            {
                var effectiveExistingBranch = string.IsNullOrWhiteSpace(existing.SourceBranch)
                    ? defaultBranch
                    : existing.SourceBranch;
                if (!string.Equals(effectiveExistingBranch, sourceBranch, StringComparison.Ordinal))
                    return MapToDto(existing);
            }

            existing.YamlDefinition = yamlContent;
            if (!string.IsNullOrWhiteSpace(configuredSourceBranch))
                existing.SourceBranch = string.Equals(configuredSourceBranch, defaultBranch, StringComparison.Ordinal)
                    ? null
                    : configuredSourceBranch;
            existing.TriggerType = triggerType.Equals("webhook", StringComparison.OrdinalIgnoreCase)
                ? PipelineTriggerType.Webhook
                : triggerType.Equals("schedule", StringComparison.OrdinalIgnoreCase)
                    ? PipelineTriggerType.Schedule
                    : PipelineTriggerType.Manual;
            existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            // Git is authoritative for project pipelines. When an authenticated pusher changes the YAML,
            // automated execution must be authorized with THAT revision author's Server.Admin grants,
            // never with a previous owner's broader grants. Background syncs have no current principal and
            // keep a resolvable owner; they repair only legacy/unresolvable ownership as before.
            var syncActor = CurrentUsername();
            if (!string.IsNullOrEmpty(syncActor))
            {
                if (!string.Equals(syncActor, existing.CreatedByUsername, StringComparison.Ordinal))
                {
                    logger.LogInformation(
                        "Transferred pipeline '{Name}' (id {Id}) automated-run ownership from '{Old}' to git revision author '{New}'",
                        existing.Name, existing.Id, existing.CreatedByUsername, syncActor);
                    existing.CreatedByUsername = syncActor;
                }
            }
            else if (await IsUnresolvableOwnerAsync(existing.CreatedByUsername, ct).ConfigureAwait(false))
            {
                var repaired = await ResolveSyncOwnerAsync(projectId, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(repaired)) existing.CreatedByUsername = repaired;
            }

            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            await BroadcastPipelineChangeAsync(existing, EntityChangeOps.Updated, ct).ConfigureAwait(false);
            return MapToDto(existing);
        }

        // F-INF-02: own the auto-synced pipeline with a REAL user (the current pusher, else the project's org
        // Owner) rather than the non-existent "system", which the F-EXEC-1b gate can never resolve and which
        // therefore blocked chained release triggers. Fall back to "system" only if nothing resolves.
        var owner = await ResolveSyncOwnerAsync(projectId, ct).ConfigureAwait(false);

        var pipeline = new Pipeline
        {
            Name = name,
            YamlDefinition = yamlContent,
            SourceBranch = string.IsNullOrWhiteSpace(configuredSourceBranch ?? sourceBranch)
                || string.Equals(configuredSourceBranch ?? sourceBranch, defaultBranch, StringComparison.Ordinal)
                    ? null
                    : configuredSourceBranch ?? sourceBranch,
            TriggerType = triggerType.Equals("webhook", StringComparison.OrdinalIgnoreCase)
                ? PipelineTriggerType.Webhook
                : triggerType.Equals("schedule", StringComparison.OrdinalIgnoreCase)
                    ? PipelineTriggerType.Schedule
                    : PipelineTriggerType.Manual,
            ProjectId = projectId,
            CreatedByUsername = string.IsNullOrEmpty(owner) ? "system" : owner
        };
        await repo.AddPipelineAsync(pipeline, ct).ConfigureAwait(false);
        await BroadcastPipelineChangeAsync(pipeline, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return MapToDto(pipeline);
    }

    private async Task BroadcastPipelineChangeAsync(Pipeline pipeline, string operation, CancellationToken ct)
    {
        var organizationId = await repo.GetPipelineOwnerOrganizationIdAsync(
            pipeline.ProjectId, pipeline.EnvironmentId, pipeline.ProjectServerId, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(
            ResourceType.Pipeline, pipeline.Id, operation, ct, organizationId).ConfigureAwait(false);
    }

    // Owner resolution shared by create and self-heal: the current pusher, else the project's org Owner.
    private async Task<string?> ResolveSyncOwnerAsync(int projectId, CancellationToken ct)
    {
        var owner = CurrentUsername();
        if (string.IsNullOrEmpty(owner))
            owner = await repo.GetProjectOwnerUsernameAsync(projectId, ct).ConfigureAwait(false);
        return owner;
    }

    // An owner the F-EXEC-1b gate can never authorize: the "system" sentinel, a blank value, or a username
    // that no longer maps to an active user. These are the exact rows that strand chained/automated triggers.
    private async Task<bool> IsUnresolvableOwnerAsync(string? owner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.Equals(owner, "system", StringComparison.OrdinalIgnoreCase))
            return true;
        return !await repo.IsActiveUsernameAsync(owner, ct).ConfigureAwait(false);
    }

    // F-EXEC-1b: current authenticated principal (same accessor pattern as AuditService).
    // Pipeline create/update endpoints are [Authorize]d, so this is non-null in production.
    private string? CurrentUsername() =>
        httpContextAccessor.HttpContext?.User?.Identity?.Name;

    private static PipelineDto MapToDto(Pipeline p)
    {
        var lastRun = p.Runs.FirstOrDefault();
        return new PipelineDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            YamlDefinition = p.YamlDefinition,
            TriggerType = p.TriggerType,
            ProjectId = p.ProjectId,
            ProjectName = p.Project?.Name,
            SourceBranch = p.SourceBranch,
            EnvironmentId = p.EnvironmentId,
            EnvironmentName = p.Environment?.Name,
            ProjectServerId = p.ProjectServerId,
            ProjectServerName = p.ProjectServer?.DisplayName,
            LastRunStatus = lastRun?.Status,
            LastRunAt = lastRun?.StartedAt,
            RecentRuns = p.Runs.Select(r =>
            {
                var firstStep = r.StepRuns.FirstOrDefault(s => !s.IsSystem && s.ServerId != null);
                return new PipelineRunSummaryDto
                {
                    Id = r.Id,
                    Status = r.Status,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    ServerName = firstStep?.Server?.Name,
                    ServerOs = firstStep?.Server?.OsDescription
                };
            }).ToList(),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }
}
