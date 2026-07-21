// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

// Core pipeline data access (paged lists, CRUD, run/step/template/artifact/approval queries).
// Extracted from the former PipelineRepository partial into a focused collaborator; the
// PipelineRepository facade composes this plus the resolver/coverage/lifecycle collaborators.
internal sealed class PipelineCoreRepository(AppDbContext db, TimeProvider timeProvider, ILogger logger)
{
    private readonly PipelineArtifactRepository _artifacts = new(db);

    public async Task<(List<Pipeline> Items, int TotalCount)> GetPipelinesPagedAsync(
        string? search, PipelineTriggerType? triggerType, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Pipelines.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(p => accessibleIds.Contains(p.Id));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search));

        if (triggerType.HasValue)
            query = query.Where(p => p.TriggerType == triggerType.Value);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(p => p.Project)
            .Include(p => p.Environment)
            .Include(p => p.ProjectServer)
            .Include(p => p.Runs.OrderByDescending(r => r.StartedAt).Take(5))
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<PipelineDto> Items, int TotalCount)> GetPipelinesPagedProjectedAsync(
        string? search, PipelineTriggerType? triggerType, int? environmentId, int? projectServerId, int? projectId,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Pipelines.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(p => accessibleIds.Contains(p.Id));

        // X4F9: project scope filters server-side on ProjectId (the project-detail list used to load the
        // whole ProjectDetailDto just for .Pipelines). Independent of the env/server scope filters below.
        if (projectId.HasValue)
            query = query.Where(p => p.ProjectId == projectId.Value);

        if (environmentId.HasValue)
            query = query.Where(p => p.EnvironmentId == environmentId.Value);
        else if (projectServerId.HasValue)
            query = query.Where(p => p.ProjectServerId == projectServerId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search));

        if (triggerType.HasValue)
            query = query.Where(p => p.TriggerType == triggerType.Value);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PipelineDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                YamlDefinition = p.YamlDefinition,
                TriggerType = p.TriggerType,
                ProjectId = p.ProjectId,
                ProjectName = p.Project != null ? p.Project.Name : null,
                SourceBranch = p.SourceBranch,
                EnvironmentId = p.EnvironmentId,
                EnvironmentName = p.Environment != null ? p.Environment.Name : null,
                ProjectServerId = p.ProjectServerId,
                ProjectServerName = p.ProjectServer != null ? p.ProjectServer.DisplayName : null,
                LastRunStatus = p.Runs.OrderByDescending(r => r.StartedAt).Select(r => (PipelineStatus?)r.Status).FirstOrDefault(),
                LastRunAt = p.Runs.OrderByDescending(r => r.StartedAt).Select(r => (DateTime?)r.StartedAt).FirstOrDefault(),
                RecentRuns = p.Runs.OrderByDescending(r => r.StartedAt).Take(5).Select(r => new PipelineRunSummaryDto
                {
                    Id = r.Id,
                    Status = r.Status,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    ServerName = r.StepRuns.Where(s => !s.IsSystem && s.ServerId != null).OrderBy(s => s.Order).Select(s => s.Server!.Name).FirstOrDefault(),
                    ServerOs = r.StepRuns.Where(s => !s.IsSystem && s.ServerId != null).OrderBy(s => s.Order).Select(s => s.Server!.OsDescription).FirstOrDefault()
                }).ToList(),
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<Pipeline?> GetPipelineWithRunsAsync(int id, CancellationToken ct = default)
    {
        return await db.Pipelines
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Include(p => p.Project)
            .Include(p => p.Environment)
            .Include(p => p.ProjectServer)
            .Include(p => p.Runs.OrderByDescending(r => r.StartedAt).Take(5))
                .ThenInclude(r => r.StepRuns.Where(s => !s.IsSystem && s.ServerId != null).OrderBy(s => s.Order).Take(1))
                    .ThenInclude(s => s.Server)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Pipeline?> FindPipelineAsync(int id, CancellationToken ct = default)
    {
        return await db.Pipelines
            .Where(p => p.Id == id)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }


    public async Task AddPipelineAsync(Pipeline pipeline, CancellationToken ct = default)
    {
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemovePipelineAsync(Pipeline pipeline, CancellationToken ct = default)
    {
        db.Pipelines.Remove(pipeline);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddPipelineRunAsync(PipelineRun run, CancellationToken ct = default)
    {
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public void TrackPipelineStepRun(PipelineStepRun stepRun)
    {
        db.PipelineStepRuns.Add(stepRun);
    }

    public async Task<List<PipelineStepRun>> GetPendingStepRunsAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && s.Status == TaskExecutionStatus.Pending)
            .OrderBy(s => s.Order)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // Server resolution is delegated to PipelineServerResolver.

    public void TrackTask(ServerTask task)
    {
        db.Tasks.Add(task);
    }

    public async Task<List<PipelineRun>> GetRunsAsync(int pipelineId, int count, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.PipelineId == pipelineId)
            .Include(r => r.Pipeline)
            .Include(r => r.StepRuns)
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // Server-side projection (PipelineRunHelpers.RunListProjection): the run LIST only renders
    // scalar run fields + step summary chips, so the heavy per-run payload (YamlSnapshot,
    // resolved-variables JSON, step commands, output variables, artifacts) never leaves the
    // database - and output variables never bypass the secret masking GetRunAsync applies.
    public async Task<(List<PipelineRunDto> Items, int TotalCount)> GetRunsPagedAsync(int pipelineId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.PipelineRuns.AsNoTracking().Where(r => r.PipelineId == pipelineId);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(PipelineRunHelpers.RunListProjection)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task<PipelineRun?> GetRunDetailAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Pipeline)
                .ThenInclude(p => p.Project)
            .Include(r => r.StepRuns.OrderBy(s => s.Order))
                .ThenInclude(s => s.Server)
            .Include(r => r.StepRuns.OrderBy(s => s.Order))
                .ThenInclude(s => s.Task)
            .Include(r => r.TestResults)
            .Include(r => r.CoverageResults)
            .Include(r => r.LintResults)
            .Include(r => r.RunMetrics)
            .Include(r => r.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == runId, ct).ConfigureAwait(false);
    }

    public async Task<List<StepOutputProjection>> GetSuccessfulStepOutputsAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .AsNoTracking()
            .Where(s => s.PipelineRunId == runId && s.Status == TaskExecutionStatus.Success && s.OutputVariablesJson != null)
            .Select(s => new StepOutputProjection(s.StageName, s.StepName, s.OutputVariablesJson))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PipelineRun?> GetPipelineRunWithPipelineAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Id == runId)
            .Include(r => r.Pipeline)
                .ThenInclude(p => p!.Project)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<int?> FindTriggeredRunIdByPipelineNameAsync(int parentRunId, string pipelineName, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns.AsNoTracking()
            .Where(s => s.PipelineRunId == parentRunId && s.TriggeredRunId != null)
            .Join(db.PipelineRuns.AsNoTracking(), s => s.TriggeredRunId, r => r.Id, (_, r) => r)
            .Where(r => r.Pipeline.Name == pipelineName)
            .OrderByDescending(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<int?> GetPipelineIdForRunAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => (int?)r.PipelineId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsServerAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns.AsNoTracking()
            .AnyAsync(s => s.PipelineRunId == runId && s.ServerId == serverId, ct).ConfigureAwait(false);
    }

    public async Task<int?> GetPipelineIdForApprovalAsync(int approvalId, CancellationToken ct = default)
    {
        return await db.PipelineApprovals.AsNoTracking()
            .Where(a => a.Id == approvalId)
            .Select(a => (int?)a.PipelineRun.PipelineId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<int?> GetPipelineIdForArtifactAsync(int artifactId, CancellationToken ct = default)
    {
        return await db.PipelineArtifacts.AsNoTracking()
            .Where(a => a.Id == artifactId)
            .Select(a => (int?)a.PipelineRun.PipelineId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> AreAllStepsInStageCompletedAsync(int runId, string stageName, CancellationToken ct = default)
    {
        return !await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && s.StageName == stageName)
            .AnyAsync(s => s.Status != TaskExecutionStatus.Success && s.Status != TaskExecutionStatus.Failed && s.Status != TaskExecutionStatus.Timeout && s.Status != TaskExecutionStatus.Cancelled, ct)
            .ConfigureAwait(false);
    }

    // Tracked (no AsNoTracking) so the trigger-completion handler can mutate the returned steps and save.
    public async Task<List<PipelineStepRun>> FindStepRunsByTriggeredRunIdAsync(int triggeredRunId, CancellationToken ct = default)
        => await db.PipelineStepRuns
            .Where(s => s.TriggeredRunId == triggeredRunId)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<int>> GetTriggeredChildRunIdsAsync(int parentRunId, CancellationToken ct = default)
        => await db.PipelineStepRuns.AsNoTracking()
            .Where(s => s.PipelineRunId == parentRunId && s.TriggeredRunId != null)
            .Select(s => s.TriggeredRunId!.Value)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<bool> HasAnyStepFailedInStageAsync(int runId, string stageName, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && s.StageName == stageName)
            .AnyAsync(s => s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasAnyFailedStepInRunAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && (s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout) && !s.ContinueOnError)
            .AnyAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasAnySucceededStepInRunAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && !s.IsSystem && s.Status == TaskExecutionStatus.Success)
            .AnyAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> GetCompletedStageNamesAsync(int runId, CancellationToken ct = default)
    {
        var allStageSteps = await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId)
            .GroupBy(s => s.StageName)
            .Select(g => new
            {
                StageName = g.Key,
                AllCompleted = g.All(s => s.Status == TaskExecutionStatus.Success || s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout || s.Status == TaskExecutionStatus.Cancelled),
                AllPassedOrContinuable = g.All(s => s.Status == TaskExecutionStatus.Success
                    || s.Status == TaskExecutionStatus.Cancelled
                    || ((s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout) && s.ContinueOnError))
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return allStageSteps
            .Where(s => s.AllCompleted && s.AllPassedOrContinuable)
            .Select(s => s.StageName)
            .ToList();
    }

    public async Task<List<string>> GetTerminalStageNamesAsync(int runId, CancellationToken ct = default)
    {
        var allStageSteps = await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId)
            .GroupBy(s => s.StageName)
            .Select(g => new
            {
                StageName = g.Key,
                AllTerminal = g.All(s => s.Status == TaskExecutionStatus.Success
                    || s.Status == TaskExecutionStatus.Failed
                    || s.Status == TaskExecutionStatus.Timeout
                    || s.Status == TaskExecutionStatus.Cancelled)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return allStageSteps
            .Where(s => s.AllTerminal)
            .Select(s => s.StageName)
            .ToList();
    }

    public async Task<bool> IsRunStillRunningAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .AnyAsync(r => r.Id == runId && r.Status == PipelineStatus.Running, ct)
            .ConfigureAwait(false);
    }

    public async Task UpdatePipelineRunStatusAsync(int runId, PipelineStatus status, CancellationToken ct = default)
    {
        // Safety net: if marking as Failed but no step actually failed or timed out (all Success/Cancelled)
        // AND at least one non-system step succeeded → override to Success.
        // Timeout counts as a failure here so a timed-out step can never be masked back to green.
        if (status == PipelineStatus.Failed)
        {
            try
            {
                var hasFailed = await db.PipelineStepRuns
                    .AnyAsync(s => s.PipelineRunId == runId && (s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout), ct).ConfigureAwait(false);
                var hasSucceeded = await db.PipelineStepRuns
                    .AnyAsync(s => s.PipelineRunId == runId && !s.IsSystem && s.Status == TaskExecutionStatus.Success, ct).ConfigureAwait(false);
                logger.LogDebug("Run {RunId} safety net: hasFailed={H1} hasSucceeded={H2}", runId, hasFailed, hasSucceeded);
                if (!hasFailed && hasSucceeded)
                    status = PipelineStatus.Success;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Safety net query failed for run {RunId}", runId);
            }
        }

        // Atomic update: only transition from Running to a terminal status.
        // Prevents concurrent AdvanceStageAsync calls from racing (first writer wins).
        var now = timeProvider.GetUtcNow().UtcDateTime;
        int rows;
        if (db.Database.IsRelational())
        {
            rows = await db.PipelineRuns
                .Where(r => r.Id == runId && r.Status == PipelineStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.CompletedAt, status is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled ? now : (DateTime?)null), ct)
                .ConfigureAwait(false);

            // ExecuteUpdateAsync bypasses the change tracker. Evict any tracked PipelineRun
            // entity so a subsequent SaveChangesAsync in the same scope doesn't overwrite
            // the DB with a stale Status value.
            var tracked = db.ChangeTracker.Entries<PipelineRun>()
                .FirstOrDefault(e => e.Entity.Id == runId);
            if (tracked is not null)
                tracked.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
        else
        {
            // InMemory (unit tests)
            var run = await db.PipelineRuns.FirstOrDefaultAsync(r => r.Id == runId && r.Status == PipelineStatus.Running, ct).ConfigureAwait(false);
            if (run is not null)
            {
                run.Status = status;
                if (status is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled)
                    run.CompletedAt = now;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                rows = 1;
            }
            else rows = 0;
        }
        if (rows == 0)
            logger.LogDebug("Run {RunId} status update to {Status} skipped (no longer Running)", runId, status);
    }

    public async Task AppendRunWarningsAsync(int runId, IReadOnlyCollection<string> warnings, CancellationToken ct = default)
    {
        if (warnings.Count == 0) return;

        // FindAsync returns a change-tracked entity (and reuses the identity-map instance if the
        // run is already loaded), so the WarningsJson mutation below is actually persisted.
        var run = await db.PipelineRuns.FindAsync([runId], ct).ConfigureAwait(false);
        if (run is null) return;

        List<string> existing = string.IsNullOrEmpty(run.WarningsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(run.WarningsJson) ?? [];
        existing.AddRange(warnings);
        run.WarningsJson = JsonSerializer.Serialize(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // Pool, environment and organization resolution is delegated to PipelineServerResolver.

    public async Task CancelPendingStepRunsAsync(int runId, CancellationToken ct = default)
    {
        var pendingSteps = await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId &&
                        (s.Status == TaskExecutionStatus.Pending || s.Status == TaskExecutionStatus.Assigned))
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var step in pendingSteps)
        {
            step.Status = TaskExecutionStatus.Cancelled;
            step.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ResetFailedStepRunsAsync(int runId, CancellationToken ct = default)
    {
        var failedSteps = await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && (s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout))
            .ToListAsync(ct).ConfigureAwait(false);

        if (failedSteps.Count == 0) return 0;

        foreach (var step in failedSteps)
        {
            step.Status = TaskExecutionStatus.Pending;
            step.ExitCode = null;
            step.StartedAt = null;
            step.CompletedAt = null;
        }

        // Atomically re-open the run so the existing scheduler can pick the steps back up.
        var run = await db.PipelineRuns.FindAsync([runId], ct).ConfigureAwait(false);
        if (run is not null)
        {
            run.Status = PipelineStatus.Running;
            run.CompletedAt = null;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return failedSteps.Count;
    }

    public Task<List<PipelineArtifact>> GetArtifactsAsync(int runId, CancellationToken ct = default)
        => _artifacts.GetArtifactsAsync(runId, ct);

    public Task AddArtifactAsync(PipelineArtifact artifact, CancellationToken ct = default)
        => _artifacts.AddArtifactAsync(artifact, ct);

    public async Task<List<PipelineApproval>> GetApprovalsAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineApprovals
            .AsNoTracking()
            .Where(a => a.PipelineRunId == runId)
            .Include(a => a.Environment)
            .Include(a => a.ResolvedByUser)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PipelineApproval?> FindApprovalAsync(int approvalId, CancellationToken ct = default)
    {
        return await db.PipelineApprovals
            .Include(a => a.Environment)
            .Include(a => a.ResolvedByUser)
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct).ConfigureAwait(false);
    }

    public async Task AddApprovalAsync(PipelineApproval approval, CancellationToken ct = default)
    {
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Data.Entities.Environment?> FindEnvironmentByNameAsync(string name, CancellationToken ct = default)
    {
        return await db.Environments
            .Include(e => e.Servers).ThenInclude(es => es.Server)
            .Include(e => e.Checks)
            .FirstOrDefaultAsync(e => e.Name == name, ct).ConfigureAwait(false);
    }

    public async Task<List<TestResult>> GetTestResultsAsync(int runId, CancellationToken ct = default)
    {
        return await db.TestResults
            .AsNoTracking()
            .Where(t => t.PipelineRunId == runId)
            .OrderBy(t => t.TestSuite)
            .ThenBy(t => t.TestName)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddTestResultsAsync(IEnumerable<TestResult> results, CancellationToken ct = default)
    {
        db.TestResults.AddRange(results);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<PipelineStepRun>> GetFailedStepRunsInStageAsync(int runId, string stageName, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && s.StageName == stageName && (s.Status == TaskExecutionStatus.Failed || s.Status == TaskExecutionStatus.Timeout))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<EnvironmentCheck>> GetEnvironmentChecksAsync(int environmentId, CancellationToken ct = default)
    {
        return await db.EnvironmentChecks
            .AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // Pipeline lifecycle queries (retention, webhook/schedule, active-run) live in
    // PipelineRepository.Lifecycle.cs to stay within the 600-line file budget.

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
