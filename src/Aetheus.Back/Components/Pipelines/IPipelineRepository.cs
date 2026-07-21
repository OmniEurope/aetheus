// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Pipelines;

public sealed record StepOutputProjection(string StageName, string StepName, string? OutputVariablesJson);

public interface IPipelineRepository
{
    Task<(List<Pipeline> Items, int TotalCount)> GetPipelinesPagedAsync(
        string? search, PipelineTriggerType? triggerType, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    /// <summary>
    /// Projected variant of <see cref="GetPipelinesPagedAsync"/> that returns <see cref="PipelineDto"/>
    /// directly via a <c>.Select()</c> projection, avoiding loading full entity graphs.
    /// </summary>
    Task<(List<PipelineDto> Items, int TotalCount)> GetPipelinesPagedProjectedAsync(
        string? search, PipelineTriggerType? triggerType, int? environmentId, int? projectServerId, int? projectId,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<List<PipelineDto>> GetPipelinesForDependencyGraphAsync(List<int>? accessibleIds = null, CancellationToken ct = default);
    Task<(List<PipelineDto> Items, List<PipelineDto> Identities, int TotalCount)> GetPipelineDependencyPageAsync(
        PipelinePaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<Pipeline?> GetPipelineWithRunsAsync(int id, CancellationToken ct = default);

    Task<Pipeline?> FindPipelineAsync(int id, CancellationToken ct = default);

    Task<Pipeline?> FindPipelineByNameAndProjectAsync(string name, int projectId, CancellationToken ct = default);

    Task AddPipelineAsync(Pipeline pipeline, CancellationToken ct = default);

    Task RemovePipelineAsync(Pipeline pipeline, CancellationToken ct = default);

    Task AddPipelineRunAsync(PipelineRun run, CancellationToken ct = default);

    /// <summary>Tracks a step run on the change tracker. Caller must invoke <see cref="SaveChangesAsync"/> to flush.</summary>
    void TrackPipelineStepRun(PipelineStepRun stepRun);

    Task<List<PipelineStepRun>> GetPendingStepRunsAsync(int runId, CancellationToken ct = default);

    /// <summary>All pipelines owned by an environment (used by the git-sync copy on project link).</summary>
    Task<List<Pipeline>> GetPipelinesByEnvironmentAsync(int environmentId, CancellationToken ct = default);

    /// <summary>S-FEAT-N5KQ: the (name, linked-project) of an environment, or null when the environment
    /// has no linked project - used to copy a newly-added pipeline into the already-linked project's git.</summary>
    Task<(string Name, int ProjectId)?> GetEnvironmentCopyTargetAsync(int environmentId, CancellationToken ct = default);

    // The optional requiredOs filters candidates to a single OS family (pipeline `os:` typing).
    // OsType.Unknown means "no constraint" - the pre-typing behavior.
    Task<Server?> FindOnlineServerByAgentAsync(string agent, OsType requiredOs = OsType.Unknown, CancellationToken ct = default);
    Task<Server?> FindOnlineServerByAgentInOrganizationAsync(string agent, OsType requiredOs, int organizationId, CancellationToken ct = default);

    Task<Server?> FindOnlineServerInPoolAsync(string poolName, OsType requiredOs = OsType.Unknown, CancellationToken ct = default);
    Task<Server?> FindOnlineServerInPoolInOrganizationAsync(string poolName, OsType requiredOs, int organizationId, CancellationToken ct = default);

    Task<Server?> FindOnlineServerInEnvironmentAsync(string environmentName, OsType requiredOs = OsType.Unknown, CancellationToken ct = default);
    Task<Server?> FindOnlineServerInEnvironmentInOrganizationAsync(string environmentName, OsType requiredOs, int organizationId, CancellationToken ct = default);

    /// <summary>Any online pipeline-runner, scoped to <paramref name="organizationId"/> when given.
    /// Always-runnable fallback when a stage's specific selector matches nothing.</summary>
    Task<Server?> FindAnyOnlineRunnerAsync(int? organizationId, OsType requiredOs = OsType.Unknown, CancellationToken ct = default);
    /// <summary>Cross-agent deploy targeting (fail-closed): pool > environment > agent precedence over
    /// DeploymentTargetAvailable servers only, with a deploy-capable org fallback. Never a plain runner.</summary>
    Task<Server?> FindOnlineDeployTargetAsync(string? pool, string? environment, string? agent, OsType requiredOs, int? organizationId, CancellationToken ct = default);
    /// <summary>Returns every server that the stage resolver can select, using the same selector,
    /// organization, OS and deploy-capability rules as execution.</summary>
    Task<List<int>> FindCandidateTargetServerIdsAsync(string? pool, string? environment, string? agent, OsType requiredOs, int? organizationId, bool deploymentStage, CancellationToken ct = default);
    Task<Server?> FindOnlineServerByIdAsync(int serverId, OsType requiredOs = OsType.Unknown, CancellationToken ct = default);
    Task<int?> GetRunAffinityServerIdAsync(int runId, CancellationToken ct = default);
    Task<int?> GetStageProducerServerIdAsync(int runId, string stageName, CancellationToken ct = default);

    /// <summary>Organization owning a pipeline (via Project | Environment→Project | ProjectServer→Project).
    /// Null if unresolvable (legacy orphan pipeline).</summary>
    Task<int?> GetPipelineOrganizationIdAsync(int pipelineId, CancellationToken ct = default);

    Task<int?> GetPipelineOwnerOrganizationIdAsync(
        int? projectId, int? environmentId, int? projectServerId, CancellationToken ct = default);

    /// <summary>Project owning a pipeline (direct ProjectId, or via Environment→ProjectId,
    /// or via ProjectServer→ProjectId). Null if unresolvable (legacy orphan).</summary>
    Task<int?> GetPipelineProjectIdAsync(Pipeline pipeline, CancellationToken ct = default);
    // S-FEAT-16: project's default release version pattern, or null when unset.
    Task<string?> GetProjectReleasePatternAsync(int projectId, CancellationToken ct = default);
    // F-INF-02: username of the project's owning-organization Owner, or null when unresolvable.
    Task<string?> GetProjectOwnerUsernameAsync(int projectId, CancellationToken ct = default);
    // F-INF-02b: true when the username maps to an active user (used to detect a poisoned pipeline owner).
    Task<bool> IsActiveUsernameAsync(string username, CancellationToken ct = default);

    // F-EXEC-1: candidate-target enumeration for authorization. Unlike the FindOnlineServer*
    // methods (which pick one *online* server at scheduling time), these return EVERY server a
    // stage selector could resolve to regardless of status, so a pre-run Server.Admin check
    // cannot be bypassed via the online/offline race.
    Task<List<int>> FindServerIdsByAgentAsync(string? agent, CancellationToken ct = default);

    Task<List<int>> FindServerIdsInPoolAsync(string poolName, CancellationToken ct = default);

    Task<List<int>> FindServerIdsInEnvironmentAsync(string environmentName, CancellationToken ct = default);

    /// <summary>Tracks a server task on the change tracker. Caller must invoke <see cref="SaveChangesAsync"/> to flush.</summary>
    void TrackTask(ServerTask task);

    Task<List<PipelineRun>> GetRunsAsync(int pipelineId, int count, CancellationToken ct = default);
    Task<(List<PipelineRunDto> Items, int TotalCount)> GetRunsPagedAsync(int pipelineId, int page, int pageSize, CancellationToken ct = default);
    Task<List<PipelineRunDto>> GetActiveRunsAsync(List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default);
    Task<List<PipelineRunDto>> GetRecentRunsAsync(List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default);

    Task<PipelineRun?> GetRunDetailAsync(int runId, CancellationToken ct = default);

    Task<List<StepOutputProjection>> GetSuccessfulStepOutputsAsync(int runId, CancellationToken ct = default);

    Task<PipelineRun?> GetPipelineRunWithPipelineAsync(int runId, CancellationToken ct = default);

    Task<bool> AreAllStepsInStageCompletedAsync(int runId, string stageName, CancellationToken ct = default);

    /// <summary>Trigger-step orchestration: the (tracked) step runs that launched <paramref name="triggeredRunId"/>
    /// and are still waiting on it. Used by <c>PipelineRunCompletedTriggerHandler</c> to complete the parent step.</summary>
    Task<List<PipelineStepRun>> FindStepRunsByTriggeredRunIdAsync(int triggeredRunId, CancellationToken ct = default);

    /// <summary>Direct child runs launched by trigger steps of the supplied parent run.</summary>
    Task<List<int>> GetTriggeredChildRunIdsAsync(int parentRunId, CancellationToken ct = default);

    /// <summary>Finds the child run of a named pipeline which was triggered by a parent orchestration
    /// run. Used to resolve a verified build artifact across CI → QA → Deploy without trusting an id
    /// supplied by YAML.</summary>
    Task<int?> FindTriggeredRunIdByPipelineNameAsync(int parentRunId, string pipelineName, CancellationToken ct = default);

    /// <summary>Trigger steps still Running (and older than a grace period) that wait on a child run.
    /// The reconcile sweeper re-checks them because a trigger step has no ServerTask and so is invisible
    /// to the task-timeout sweep.</summary>
    Task<List<PipelineStepRun>> GetStuckRunningTriggerStepsAsync(DateTime startedBefore, CancellationToken ct = default);

    /// <summary>Terminal/current status of the given runs, for cheaply reconciling waiting trigger steps.</summary>
    Task<Dictionary<int, PipelineStatus>> GetRunStatusesByIdsAsync(IReadOnlyCollection<int> runIds, CancellationToken ct = default);

    /// <summary>Active runs old enough to require a scheduler retry, with pending pipeline steps but no
    /// assigned/running step, in-flight server task, child trigger or approval that could wake them.</summary>
    Task<List<int>> GetStalledSchedulableRunIdsAsync(DateTime startedBefore, CancellationToken ct = default);

    /// <summary>Runs wedged in Running past <paramref name="startedBefore"/> with no in-flight task and no
    /// trigger step waiting on a child - i.e. runs that can no longer make progress. The reconcile sweep
    /// force-fails them.</summary>
    Task<List<int>> GetStuckRunningRunIdsAsync(DateTime startedBefore, CancellationToken ct = default);

    Task<bool> HasAnyStepFailedInStageAsync(int runId, string stageName, CancellationToken ct = default);

    /// <summary>True if any step (across any stage) has already failed for this run. Used to short-circuit
    /// the per-stage failure poll when evaluating conditional expressions.</summary>
    Task<bool> HasAnyFailedStepInRunAsync(int runId, CancellationToken ct = default);

    Task<bool> HasAnySucceededStepInRunAsync(int runId, CancellationToken ct = default);

    Task<List<string>> GetCompletedStageNamesAsync(int runId, CancellationToken ct = default);

    /// <summary>Stages whose steps have all reached a terminal status, including hard failures.
    /// Used to release <c>always()</c>/<c>failed()</c> handlers and the system cleanup without
    /// treating failed stages as successful dependencies for ordinary stages.</summary>
    Task<List<string>> GetTerminalStageNamesAsync(int runId, CancellationToken ct = default);

    Task<bool> IsRunStillRunningAsync(int runId, CancellationToken ct = default);

    Task<bool> HasAnyRunningStepInRunAsync(int runId, CancellationToken ct = default);

    Task<List<string>> GetActiveStageNamesAsync(int runId, CancellationToken ct = default);

    Task UpdatePipelineRunStatusAsync(int runId, PipelineStatus status, CancellationToken ct = default);
    Task<bool> TryTransitionPipelineRunStatusAsync(int runId, PipelineStatus expectedStatus, PipelineStatus newStatus, CancellationToken ct = default);

    /// <summary>Appends warnings to a run's <c>WarningsJson</c> via a tracked entity and persists immediately.
    /// The no-server failure path must use this rather than mutating the result of
    /// <see cref="GetPipelineRunWithPipelineAsync"/> (which is <c>AsNoTracking</c> - mutations there are
    /// silently dropped, leaving a failed run with no visible error message).</summary>
    Task AppendRunWarningsAsync(int runId, IReadOnlyCollection<string> warnings, CancellationToken ct = default);

    Task<List<PipelineTemplate>> GetTemplatesAsync(CancellationToken ct = default);

    Task<PipelineTemplate?> GetTemplateAsync(int id, CancellationToken ct = default);

    Task<PipelineTemplate?> FindTemplateAsync(int id, CancellationToken ct = default);

    Task AddTemplateAsync(PipelineTemplate template, CancellationToken ct = default);

    Task RemoveTemplateAsync(PipelineTemplate template, CancellationToken ct = default);

    Task CancelPendingStepRunsAsync(int runId, CancellationToken ct = default);

    /// <summary>Resets every <c>Failed</c> step run of a run back to <c>Pending</c> and re-opens the
    /// run (Status → Running, CompletedAt → null) so the scheduler reruns only the failed steps.
    /// Returns the number of steps reset (0 if none).</summary>
    Task<int> ResetFailedStepRunsAsync(int runId, CancellationToken ct = default);

    Task<List<PipelineArtifact>> GetArtifactsAsync(int runId, CancellationToken ct = default);

    Task AddArtifactAsync(PipelineArtifact artifact, CancellationToken ct = default);

    Task<List<PipelineApproval>> GetApprovalsAsync(int runId, CancellationToken ct = default);

    Task<PipelineApproval?> FindApprovalAsync(int approvalId, CancellationToken ct = default);
    Task<PipelineApproval?> TryResolveApprovalAsync(int approvalId, ApprovalStatus decision, DateTime resolvedAt,
        int? resolvedByUserId, string? comments, CancellationToken ct = default);

    Task AddApprovalAsync(PipelineApproval approval, CancellationToken ct = default);

    Task<Data.Entities.Environment?> FindEnvironmentByNameAsync(string name, CancellationToken ct = default);

    Task<PipelineTemplate?> FindTemplateByNameAsync(string name, CancellationToken ct = default);

    Task<PipelineTemplate?> FindTemplateByNameAsync(string name, int organizationId, CancellationToken ct = default);

    Task<PipelineTemplateVersion?> GetTemplateVersionAsync(int templateId, int version, CancellationToken ct = default);

    Task<(List<PipelineTemplateVersionSummaryDto> Items, int TotalCount)> GetTemplateVersionsPagedAsync(
        int templateId, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);

    Task<List<string>> GetPipelineYamlDefinitionsByOrganizationAsync(
        int organizationId, CancellationToken ct = default);
    Task<List<string>> GetTemplateVersionYamlDefinitionsByOrganizationAsync(
        int organizationId, int excludedTemplateId, CancellationToken ct = default);

    Task<List<TestResult>> GetTestResultsAsync(int runId, CancellationToken ct = default);

    Task AddTestResultsAsync(IEnumerable<TestResult> results, CancellationToken ct = default);

    Task<List<CoverageResult>> GetCoverageResultsAsync(int runId, CancellationToken ct = default);

    Task AddCoverageResultAsync(CoverageResult result, CancellationToken ct = default);

    Task<List<LintResult>> GetLintResultsAsync(int runId, CancellationToken ct = default);

    Task AddLintResultAsync(LintResult result, CancellationToken ct = default);

    Task<List<CoverageTrendRow>> GetCoverageTrendAsync(int runId, int take, CancellationToken ct = default);

    Task<List<ComplexityTrendRow>> GetComplexityTrendAsync(int runId, int take, CancellationToken ct = default);

    Task<List<CoverageTrendRow>> GetProjectCoverageTrendAsync(int projectId, int take, CancellationToken ct = default);

    Task<List<ComplexityTrendRow>> GetProjectComplexityTrendAsync(int projectId, int take, CancellationToken ct = default);

    Task<List<TestTrendRow>> GetProjectTestTrendAsync(int projectId, int take, CancellationToken ct = default);

    Task<List<RunMetric>> GetRunMetricsAsync(int runId, CancellationToken ct = default);

    Task AddRunMetricsAsync(IEnumerable<RunMetric> metrics, CancellationToken ct = default);

    Task<List<PipelineStepRun>> GetFailedStepRunsInStageAsync(int runId, string stageName, CancellationToken ct = default);

    Task<List<EnvironmentCheck>> GetEnvironmentChecksAsync(int environmentId, CancellationToken ct = default);

    Task<int> DeleteRunsOlderThanAsync(DateTime cutoff, CancellationToken ct = default);

    Task<List<Pipeline>> GetWebhookTriggeredPipelinesWithProjectAsync(CancellationToken ct = default);
    Task<List<Pipeline>> GetWebhookTriggeredPipelinesForProjectAsync(int projectId, CancellationToken ct = default);

    Task<List<Pipeline>> GetScheduledPipelinesAsync(CancellationToken ct = default);

    Task<bool> HasActiveRunAsync(int pipelineId, CancellationToken ct = default);

    Task<HashSet<int>> GetPipelineIdsWithActiveRunsAsync(CancellationToken ct = default);

    /// <summary>Returns the parent pipeline id for a run, or null if the run does not exist.</summary>
    Task<int?> GetPipelineIdForRunAsync(int runId, CancellationToken ct = default);

    /// <summary>F-06: returns true if the given server is assigned to any step run of the run.
    /// Used to authorize agent-token requests posting artifacts/test-results.</summary>
    Task<bool> IsServerAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default);

    /// <summary>Returns the parent pipeline id for an approval, or null if the approval does not exist.</summary>
    Task<int?> GetPipelineIdForApprovalAsync(int approvalId, CancellationToken ct = default);

    /// <summary>Returns the parent pipeline id for an artifact's run, or null if the artifact does not exist.</summary>
    Task<int?> GetPipelineIdForArtifactAsync(int artifactId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
