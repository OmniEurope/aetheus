// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Server resolution methods - split from <see cref="PipelineRepository"/> to stay within the
/// 600-line file budget. Covers agent/pool/environment matching, the always-runnable org fallback,
/// and the F-EXEC-1 authorization candidate enumeration.
/// </summary>
internal sealed class PipelineServerResolver(AppDbContext db)
{
    public async Task<Server?> FindOnlineServerByAgentAsync(string agent, OsType requiredOs = OsType.Unknown, CancellationToken ct = default)
    {
        // Item #11 secure-by-default: PipelineRunnerEnabled must be true for the scheduler to ever
        // dispatch to this server. The F-EXEC-1 authorization gate (FindServerIds*) is intentionally
        // NOT filtered - see entity comment.

        // When no specific agent is requested (empty / "default"), pick any online pipeline runner.
        if (string.IsNullOrEmpty(agent) || agent.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Servers
                .Where(s => s.Status == ServerStatus.Online && s.PipelineRunnerEnabled)
                .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        // Fast path: indexed name match.
        var byName = await db.Servers
            .Where(s => s.Status == ServerStatus.Online && s.PipelineRunnerEnabled && s.Name == agent)
            .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (byName is not null) return byName;

        // Tag match: load only the (small) set of online servers and do exact JSON-array
        // membership in memory. O(N_online) instead of O(N_total) substring scan.
        var candidates = await db.Servers
            .Where(s => s.Status == ServerStatus.Online && s.PipelineRunnerEnabled && s.Tags != null && s.Tags != "")
            .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
            .Select(s => new { s.Id, s.Tags })
            .ToListAsync(ct).ConfigureAwait(false);

        var matchId = candidates
            .FirstOrDefault(s => TagsHelper.DeserializeTags(s.Tags).Contains(agent, StringComparer.OrdinalIgnoreCase))
            ?.Id;

        return matchId is null
            ? null
            : await db.Servers.FirstOrDefaultAsync(s => s.Id == matchId.Value, ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindOnlineServerByAgentInOrganizationAsync(
        string agent, OsType requiredOs, int organizationId, CancellationToken ct = default)
    {
        var candidates = await db.Servers
            .Where(s => s.OrganizationId == organizationId
                        && s.Status == ServerStatus.Online
                        && s.PipelineRunnerEnabled)
            .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
            .ToListAsync(ct).ConfigureAwait(false);
        return candidates.FirstOrDefault(s => s.Name == agent
            || TagsHelper.DeserializeTags(s.Tags).Contains(agent, StringComparer.OrdinalIgnoreCase));
    }

    // F-EXEC-1: every server whose name OR tag matches the selector, ANY status. Mirrors
    // FindOnlineServerByAgentAsync minus the Online filter; used only for the pre-run
    // Server.Admin authorization gate, never for scheduling.
    public async Task<List<int>> FindServerIdsByAgentAsync(string? agent, CancellationToken ct = default)
    {
        // When no specific agent is requested (empty / "default"), return all pipeline runners.
        if (string.IsNullOrEmpty(agent) || agent.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Servers
                .Where(s => s.PipelineRunnerEnabled)
                .Select(s => s.Id)
                .ToListAsync(ct).ConfigureAwait(false);
        }

        var byName = await db.Servers
            .Where(s => s.Name == agent)
            .Select(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        var tagged = await db.Servers
            .Where(s => s.Tags != null && s.Tags != "")
            .Select(s => new { s.Id, s.Tags })
            .ToListAsync(ct).ConfigureAwait(false);

        var taggedIds = tagged
            .Where(s => TagsHelper.DeserializeTags(s.Tags).Contains(agent, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.Id);

        return byName.Union(taggedIds).ToList();
    }

    public async Task<Server?> FindOnlineServerInPoolAsync(string poolName, OsType requiredOs = OsType.Unknown, CancellationToken ct = default)
    {
        // Item #11 secure-by-default: PipelineRunnerEnabled must be true (see entity comment).
        return await db.AgentPoolServers
            .Where(aps => aps.AgentPool.Name == poolName
                          && aps.Server.Status == ServerStatus.Online
                          && aps.Server.PipelineRunnerEnabled)
            .Where(aps => requiredOs == OsType.Unknown || aps.Server.OsType == requiredOs)
            .Select(aps => aps.Server)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindOnlineServerInPoolInOrganizationAsync(
        string poolName, OsType requiredOs, int organizationId, CancellationToken ct = default)
    {
        return await db.AgentPoolServers
            .Where(aps => aps.AgentPool.Name == poolName
                          && aps.Server.OrganizationId == organizationId
                          && aps.Server.Status == ServerStatus.Online
                          && aps.Server.PipelineRunnerEnabled)
            .Where(aps => requiredOs == OsType.Unknown || aps.Server.OsType == requiredOs)
            .Select(aps => aps.Server)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindOnlineServerInEnvironmentAsync(string environmentName, OsType requiredOs = OsType.Unknown, CancellationToken ct = default)
    {
        // Item #11 secure-by-default: PipelineRunnerEnabled must be true (see entity comment).
        return await db.EnvironmentServers
            .Where(es => es.Environment.Name == environmentName
                         && es.Server.Status == ServerStatus.Online
                         && es.Server.PipelineRunnerEnabled)
            .Where(es => requiredOs == OsType.Unknown || es.Server.OsType == requiredOs)
            .Select(es => es.Server)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindOnlineServerInEnvironmentInOrganizationAsync(
        string environmentName, OsType requiredOs, int organizationId, CancellationToken ct = default)
    {
        return await db.EnvironmentServers
            .Where(es => es.Environment.Name == environmentName
                         && es.Server.OrganizationId == organizationId
                         && es.Server.Status == ServerStatus.Online
                         && es.Server.PipelineRunnerEnabled)
            .Where(es => requiredOs == OsType.Unknown || es.Server.OsType == requiredOs)
            .Select(es => es.Server)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    // Always-runnable fallback: any online pipeline-runner, scoped to the project's organization
    // when known. A project/environment pipeline must run regardless of whether a pool/environment/
    // server was specified - only a total absence of online org runners blocks it.
    public async Task<Server?> FindOnlineServerByIdAsync(int serverId, OsType requiredOs = OsType.Unknown, CancellationToken ct = default)
    {
        return await db.Servers
            .Where(s => s.Id == serverId && s.Status == ServerStatus.Online)
            .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int?> GetRunAffinityServerIdAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId && s.ServerId != null && s.Status == TaskExecutionStatus.Success)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => s.ServerId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int?> GetStageProducerServerIdAsync(int runId, string stageName, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId
                        && s.StageName == stageName
                        && s.ServerId != null
                        && s.Status == TaskExecutionStatus.Success)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => s.ServerId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<int>> FindCandidateTargetServerIdsAsync(
        string? pool, string? environment, string? agent, OsType requiredOs,
        int? organizationId, bool deploymentStage, CancellationToken ct = default)
    {
        var hasAgentSelector = !string.IsNullOrEmpty(agent)
            && !agent.Equals("default", StringComparison.OrdinalIgnoreCase);
        var hasExplicitSelector = !string.IsNullOrEmpty(pool) || !string.IsNullOrEmpty(environment) || hasAgentSelector;
        var selectedIds = new List<int>();

        if (!string.IsNullOrEmpty(pool))
        {
            var query = db.AgentPoolServers.Where(aps => aps.AgentPool.Name == pool);
            if (deploymentStage) query = query.Where(aps => aps.Server.DeploymentTargetAvailable);
            else query = query.Where(aps => aps.Server.PipelineRunnerEnabled);
            if (organizationId is { } orgId) query = query.Where(aps => aps.Server.OrganizationId == orgId);
            if (requiredOs != OsType.Unknown) query = query.Where(aps => aps.Server.OsType == requiredOs);
            selectedIds = await query.Select(aps => aps.ServerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(environment))
        {
            var query = db.EnvironmentServers.Where(es => es.Environment.Name == environment);
            if (deploymentStage) query = query.Where(es => es.Server.DeploymentTargetAvailable);
            else query = query.Where(es => es.Server.PipelineRunnerEnabled);
            if (organizationId is { } orgId) query = query.Where(es => es.Server.OrganizationId == orgId);
            if (requiredOs != OsType.Unknown) query = query.Where(es => es.Server.OsType == requiredOs);
            selectedIds = await query.Select(es => es.ServerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        }
        else if (hasAgentSelector)
        {
            var query = db.Servers.Where(s => deploymentStage
                ? s.DeploymentTargetAvailable
                : s.PipelineRunnerEnabled);
            if (organizationId is { } orgId) query = query.Where(s => s.OrganizationId == orgId);
            if (requiredOs != OsType.Unknown) query = query.Where(s => s.OsType == requiredOs);
            var candidates = await query.Select(s => new { s.Id, s.Name, s.Tags }).ToListAsync(ct).ConfigureAwait(false);
            selectedIds = candidates
                .Where(s => s.Name == agent || TagsHelper.DeserializeTags(s.Tags).Contains(agent!, StringComparer.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .Distinct()
                .ToList();
        }

        // Explicit selectors are fail-closed for both execution modes. Besides matching the online
        // resolver, this keeps the authorization candidate set exact and lets the scheduler distinguish
        // a configured-but-temporarily-offline target from a selector that never matched anything.
        if (hasExplicitSelector) return selectedIds;

        var fallback = db.Servers.Where(s => deploymentStage
            ? s.DeploymentTargetAvailable
            : s.PipelineRunnerEnabled);
        if (organizationId is { } fallbackOrgId) fallback = fallback.Where(s => s.OrganizationId == fallbackOrgId);
        if (requiredOs != OsType.Unknown) fallback = fallback.Where(s => s.OsType == requiredOs);
        var fallbackIds = await fallback.Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        return selectedIds.Union(fallbackIds).Distinct().ToList();
    }

    // Cross-agent deploy targeting - FAIL-CLOSED. Mirrors the pool > environment > agent precedence of
    // ResolveServerAsync but filters on DeploymentTargetAvailable (NOT PipelineRunnerEnabled - a pure
    // deploy host need not be a CI/CD runner) and NEVER falls back to a plain pipeline runner. The
    // org-wide fallback is itself deploy-capable-only, so a deploy can never land on a non-deploy host.
    public async Task<Server?> FindOnlineDeployTargetAsync(
        string? pool, string? environment, string? agent, OsType requiredOs, int? organizationId, CancellationToken ct = default)
    {
        // An EXPLICIT selector that resolves to no deploy-capable host must FAIL (return null), never
        // fall back to "any deploy-capable host in the org" - otherwise a mistyped/unprovisioned
        // `environment: staging` would silently deploy to the prod host. The org-wide fallback is
        // reserved for the no-selector case (a single deploy host with no explicit target).
        var hasExplicitSelector = !string.IsNullOrEmpty(pool)
            || !string.IsNullOrEmpty(environment)
            || (!string.IsNullOrEmpty(agent) && !agent.Equals("default", StringComparison.OrdinalIgnoreCase));

        Server? server = null;
        if (!string.IsNullOrEmpty(pool))
            server = await db.AgentPoolServers
                .Where(aps => aps.AgentPool.Name == pool
                              && aps.Server.Status == ServerStatus.Online
                              && aps.Server.DeploymentTargetAvailable)
                .Where(aps => organizationId == null || aps.Server.OrganizationId == organizationId.Value)
                .Where(aps => requiredOs == OsType.Unknown || aps.Server.OsType == requiredOs)
                .Select(aps => aps.Server)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(environment))
            server = await db.EnvironmentServers
                .Where(es => es.Environment.Name == environment
                             && es.Server.Status == ServerStatus.Online
                             && es.Server.DeploymentTargetAvailable)
                .Where(es => organizationId == null || es.Server.OrganizationId == organizationId.Value)
                .Where(es => requiredOs == OsType.Unknown || es.Server.OsType == requiredOs)
                .Select(es => es.Server)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(agent) && !agent.Equals("default", StringComparison.OrdinalIgnoreCase))
            server = await db.Servers
                .Where(s => s.Status == ServerStatus.Online && s.DeploymentTargetAvailable && s.Name == agent)
                .Where(s => organizationId == null || s.OrganizationId == organizationId.Value)
                .Where(s => requiredOs == OsType.Unknown || s.OsType == requiredOs)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (server is not null) return server;

        // An explicit selector that matched nothing is a hard miss - do NOT broaden to the org.
        if (hasExplicitSelector) return null;

        // No-selector org fallback: any ONLINE DEPLOY-CAPABLE server in the org. Lets a single deploy
        // host with no explicit selector still work, without ever resolving to a non-deploy runner.
        var fallback = db.Servers.Where(s => s.Status == ServerStatus.Online && s.DeploymentTargetAvailable);
        if (organizationId is { } orgId) fallback = fallback.Where(s => s.OrganizationId == orgId);
        if (requiredOs != OsType.Unknown) fallback = fallback.Where(s => s.OsType == requiredOs);
        return await fallback
            .OrderBy(s => db.Tasks.Count(t => t.ServerId == s.Id && t.Status == TaskExecutionStatus.Running))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindAnyOnlineRunnerAsync(int? organizationId, OsType requiredOs = OsType.Unknown, CancellationToken ct = default)
    {
        var query = db.Servers.Where(s => s.Status == ServerStatus.Online && s.PipelineRunnerEnabled);
        if (organizationId is { } orgId)
            query = query.Where(s => s.OrganizationId == orgId);
        if (requiredOs != OsType.Unknown)
            query = query.Where(s => s.OsType == requiredOs);
        return await query
            .OrderBy(s => db.Tasks.Count(t => t.ServerId == s.Id && t.Status == TaskExecutionStatus.Running))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    // Resolve the organization owning a pipeline by walking its owner chain
    // (Project | Environment→Project | ProjectServer→Project). Null if unresolvable (legacy orphan).
    public async Task<int?> GetPipelineOrganizationIdAsync(int pipelineId, CancellationToken ct = default)
    {
        var owner = await db.Pipelines
            .Where(p => p.Id == pipelineId)
            .Select(p => new { p.ProjectId, p.EnvironmentId, p.ProjectServerId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (owner is null) return null;

        if (owner.ProjectId is { } projectId)
            return await db.Projects.Where(p => p.Id == projectId)
                .Select(p => (int?)p.OrganizationId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (owner.EnvironmentId is { } environmentId)
            return await db.Environments.Where(e => e.Id == environmentId && e.ProjectId != null)
                .Select(e => (int?)e.Project!.OrganizationId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (owner.ProjectServerId is { } projectServerId)
            return await db.ProjectServers.Where(ps => ps.Id == projectServerId)
                .Select(ps => (int?)ps.Project.OrganizationId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return null;
    }

    public async Task<int?> GetPipelineOwnerOrganizationIdAsync(
        int? projectId, int? environmentId, int? projectServerId, CancellationToken ct = default)
    {
        if (projectId is { } directProjectId)
            return await db.Projects.Where(project => project.Id == directProjectId)
                .Select(project => (int?)project.OrganizationId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (environmentId is { } directEnvironmentId)
            return await db.Environments.Where(environment => environment.Id == directEnvironmentId
                    && environment.ProjectId != null)
                .Select(environment => (int?)environment.Project!.OrganizationId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (projectServerId is { } directProjectServerId)
            return await db.ProjectServers.Where(projectServer => projectServer.Id == directProjectServerId)
                .Select(projectServer => (int?)projectServer.Project.OrganizationId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return null;
    }

    public async Task<List<Pipeline>> GetPipelinesByEnvironmentAsync(int environmentId, CancellationToken ct = default)
    {
        return await db.Pipelines
            .AsNoTracking()
            .Where(p => p.EnvironmentId == environmentId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<(string Name, int ProjectId)?> GetEnvironmentCopyTargetAsync(int environmentId, CancellationToken ct = default)
    {
        var row = await db.Environments.AsNoTracking()
            .Where(e => e.Id == environmentId && e.ProjectId != null)
            .Select(e => new { e.Name, e.ProjectId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row?.ProjectId is { } projectId ? (row.Name, projectId) : null;
    }

    public async Task<int?> GetPipelineProjectIdAsync(Pipeline pipeline, CancellationToken ct = default)
    {
        if (pipeline.ProjectId is not null) return pipeline.ProjectId;
        if (pipeline.EnvironmentId is { } envId)
            return await db.Environments.Where(e => e.Id == envId).Select(e => e.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (pipeline.ProjectServerId is { } psId)
            return await db.ProjectServers.Where(ps => ps.Id == psId).Select(ps => (int?)ps.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return null;
    }

    // S-FEAT-16: the project's default release version pattern (null when unset → caller falls back).
    public async Task<string?> GetProjectReleasePatternAsync(int projectId, CancellationToken ct = default) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.ReleaseNumberingPattern)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    // F-INF-02: username of the project's owning-organization Owner (null if unresolvable). Used to give a
    // pipeline auto-synced from committed YAML a REAL owner, not the non-existent "system" that the F-EXEC-1b
    // authorization gate can never resolve (which is what blocked chained release triggers).
    public async Task<string?> GetProjectOwnerUsernameAsync(int projectId, CancellationToken ct = default)
    {
        var orgId = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (int?)p.OrganizationId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (orgId is null) return null;
        return await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == orgId.Value && m.Role == OrganizationRole.Owner)
            .OrderBy(m => m.UserId) // deterministic when an org has more than one Owner
            .Select(m => m.User.Username)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    // F-INF-02b: does this username still map to an active user? A pipeline owner that fails this can never
    // pass the F-EXEC-1b gate, so the sync path treats it as unresolvable and re-resolves a live owner.
    public async Task<bool> IsActiveUsernameAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        return await db.Users.AsNoTracking()
            .AnyAsync(u => u.Username == username && u.IsActive, ct).ConfigureAwait(false);
    }

    // F-EXEC-1: all pool members, ANY status (authorization gate only).
    public async Task<List<int>> FindServerIdsInPoolAsync(string poolName, CancellationToken ct = default)
    {
        return await db.AgentPoolServers
            .Where(aps => aps.AgentPool.Name == poolName)
            .Select(aps => aps.ServerId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // F-EXEC-1: all environment members, ANY status (authorization gate only).
    public async Task<List<int>> FindServerIdsInEnvironmentAsync(string environmentName, CancellationToken ct = default)
    {
        return await db.EnvironmentServers
            .Where(es => es.Environment.Name == environmentName)
            .Select(es => es.ServerId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
    }
}
