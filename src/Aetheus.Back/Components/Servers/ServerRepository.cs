// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Servers;

public class ServerRepository(AppDbContext db, TimeProvider timeProvider) : IServerRepository
{
    public async Task<(List<Server> Items, int TotalCount)> GetServersPagedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = BuildFilteredSortedServerQuery(search, sortBy, sortDescending, type, status, accessibleIds);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<ServerDto> Items, int TotalCount)> GetServersPagedProjectedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = BuildFilteredSortedServerQuery(search, sortBy, sortDescending, type, status, accessibleIds);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        // Project to an anonymous type first (Tags is a JSON string that must be
        // deserialized in memory), then map to ServerDto after materialization.
        var projected = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Hostname,
                s.OsDescription,
                s.AgentVersion,
                s.AgentProtocolVersion,
                s.AgentCapabilitiesJson,
                s.AgentUpdateReserved,
                AgentUpdateRequest = s.AgentUpdateRequests
                    .OrderByDescending(request => request.RequestedAt)
                    .Select(request => new AgentUpdateRequestSummaryDto
                    {
                        RequestId = request.Id,
                        ObservedVersion = request.ObservedVersion,
                        TargetVersion = request.TargetVersion,
                        Status = request.Status,
                        BlockingTaskCount = s.Tasks.Count(task =>
                            task.Operation != OperationKind.AgentSelfUpdate
                            && (task.Status == TaskExecutionStatus.Pending
                                || task.Status == TaskExecutionStatus.Assigned
                                || task.Status == TaskExecutionStatus.Running)),
                        RequestedAt = request.RequestedAt,
                        HandoffAt = request.HandoffAt,
                        ConfirmedAt = request.ConfirmedAt,
                        FailureCode = request.FailureCode,
                        FailureDiagnostic = request.FailureDiagnostic
                    })
                    .FirstOrDefault(),
                s.Status,
                s.Type,
                s.LastHeartbeat,
                s.Tags,
                s.CreatedAt,
                s.OrganizationId,
                s.AgentInstalledAt,
                s.PipelineRunnerEnabled,
                s.DockerAvailable,
                s.DeploymentTargetAvailable,
                s.RequireContainerIsolation,
                s.InsecureTls,
                s.CapabilityDiagnosticsJson
                ,
                s.ScannerCapabilitiesJson
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var items = projected.Select(s => new ServerDto
        {
            Id = s.Id,
            Name = s.Name,
            Hostname = s.Hostname,
            OsDescription = s.OsDescription,
            AgentVersion = s.AgentVersion,
            AgentProtocolVersion = s.AgentProtocolVersion,
            AgentCapabilities = ServerDataMapper.DeserializeDiagnostics(s.AgentCapabilitiesJson),
            AgentUpdateReserved = s.AgentUpdateReserved,
            AgentUpdateRequest = s.AgentUpdateRequest,
            Status = s.Status,
            Type = s.Type,
            LastHeartbeat = s.LastHeartbeat,
            Tags = TagsHelper.DeserializeTags(s.Tags),
            CreatedAt = s.CreatedAt,
            OrganizationId = s.OrganizationId,
            AgentInstalledAt = s.AgentInstalledAt,
            PipelineRunnerEnabled = s.PipelineRunnerEnabled,
            DockerAvailable = s.DockerAvailable,
            DeploymentTargetAvailable = s.DeploymentTargetAvailable,
            RequireContainerIsolation = s.RequireContainerIsolation,
            InsecureTls = s.InsecureTls,
            CapabilityDiagnostics = ServerDataMapper.DeserializeDiagnostics(s.CapabilityDiagnosticsJson)
            ,
            ScannerCapabilities = ServerDataMapper.DeserializeDiagnostics(s.ScannerCapabilitiesJson)
        }).ToList();

        return (items, totalCount);
    }

    // Shared filter + sort pipeline for the two paged list overloads (entity vs. projected).
    // Identical WHERE/ORDER BY clauses live here so they cannot drift apart.
    private IQueryable<Server> BuildFilteredSortedServerQuery(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status, List<int>? accessibleIds)
    {
        var query = db.Servers.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(s => accessibleIds.Contains(s.Id));

        if (type.HasValue)
            query = query.Where(s => s.Type == type.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.Name.Contains(search)
                || s.Hostname.Contains(search)
                || s.Tags.Contains(search));

        return sortBy?.ToLowerInvariant() switch
        {
            "name" => sortDescending ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name),
            "status" => sortDescending ? query.OrderByDescending(s => s.Status) : query.OrderBy(s => s.Status),
            "lastheartbeat" => sortDescending ? query.OrderByDescending(s => s.LastHeartbeat) : query.OrderBy(s => s.LastHeartbeat),
            // Default (no column header chosen): online servers first, then most-recent heartbeat first
            // - operators want the live fleet at the top and the freshest signal leading within it.
            _ => query
                .OrderBy(s => s.Status == ServerStatus.Online ? 0 : 1)
                .ThenByDescending(s => s.LastHeartbeat)
        };
    }


    public async Task<Server?> GetServerDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Include(s => s.Services)
            .Include(s => s.DockerContainers)
            .Include(s => s.DockerImages)
            .Include(s => s.DockerComposeStacks)
            .Include(s => s.DockerNetworks)
            .Include(s => s.DockerVolumes)
            .Include(s => s.ApacheState)
            .Include(s => s.ApacheModules)
            .Include(s => s.ApacheVirtualHosts)
            .Include(s => s.CertbotCertificates)
            .Include(s => s.MailState)
            .Include(s => s.TeamspeakState)
            .Include(s => s.PortsentryState)
            .Include(s => s.RkhunterState)
            .Include(s => s.RkhunterWarnings)
            .Include(s => s.Tasks.OrderByDescending(t => t.CreatedAt).Take(10))
            .Include(s => s.AgentUpdateRequests.OrderByDescending(request => request.RequestedAt).Take(1))
            .Include(s => s.Metrics.OrderByDescending(m => m.Timestamp).Take(1))
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public Task<Server?> GetServerWithCollectionsAsync(int id, CancellationToken ct = default)
        => GetServerDetailAsync(id, ct);

    public async Task<Server?> FindServerAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindServerWithTokensAsync(int id, CancellationToken ct = default)
    {
        // WHERE BEFORE Include (per architecture rules) - this is a single-row
        // load anyway, but stays consistent with the rest of the repo.
        return await db.Servers
            .Where(s => s.Id == id)
            .Include(s => s.Tokens)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveServerAsync(Server server, CancellationToken ct = default)
    {
        // Relying solely on FK CASCADE was too slow on servers with months of
        // metrics history - the single cascading transaction took long enough
        // that the browser surfaced it as a 'NetworkError' (the fetch gave up
        // before the response came back). On relational providers we set-delete
        // the heavy leaf tables first; the remaining small relations cascade.
        // InMemory (used by integration tests) still cascades via EF - it does
        // not support ExecuteDelete.
        if (db.Database.IsRelational())
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
            var id = server.Id;

            // Atomicity: the leaf set-deletes and the final server delete must all
            // commit together, so a mid-sequence failure can't leave orphaned rows.
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Restrict-FK: Vault → ProjectServer → Server - delete leaf-to-root.
            var psIds = await db.ProjectServers.Where(ps => ps.ServerId == id).Select(ps => ps.Id).ToListAsync(ct).ConfigureAwait(false);
            if (psIds.Count > 0)
            {
                await db.VaultSecretVersions.Where(v => v.VaultSecret!.Vault!.ProjectServerId != null && psIds.Contains(v.VaultSecret.Vault.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.VaultSecrets.Where(v => v.Vault.ProjectServerId != null && psIds.Contains(v.Vault.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Vaults.Where(v => v.ProjectServerId != null && psIds.Contains(v.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.EnvironmentProjectServers.Where(x => psIds.Contains(x.ProjectServerId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.ProjectServers.Where(ps => ps.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            }
            await db.ServerMetrics.Where(m => m.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ServiceInfos.Where(s => s.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerContainers.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerImages.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerNetworks.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerVolumes.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerComposeStacks.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheStates.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheModules.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheVirtualHosts.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.CertbotCertificates.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

            db.Servers.Remove(server);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        db.Servers.Remove(server);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }



















    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers
            .AnyAsync(s => s.Id == serverId, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<Server>> GetStaleOnlineServersAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - threshold;
        return await db.Servers
            .Where(s => s.Status == ServerStatus.Online && s.LastHeartbeat < cutoff)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryMarkOfflineIfStaleAsync(
        int serverId,
        DateTime observedLastHeartbeat,
        TimeSpan threshold,
        CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - threshold;
        var candidates = db.Servers.Where(server =>
            server.Id == serverId
            && server.Status == ServerStatus.Online
            && server.LastHeartbeat == observedLastHeartbeat
            && server.LastHeartbeat < cutoff);

        if (db.Database.IsRelational())
        {
            var affected = await candidates.ExecuteUpdateAsync(
                setters => setters.SetProperty(server => server.Status, ServerStatus.Offline),
                ct).ConfigureAwait(false);
            return affected == 1;
        }

        var candidate = await candidates.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (candidate is null) return false;
        candidate.Status = ServerStatus.Offline;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }



    public async Task<List<string>> GetServerNamesAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking();
        if (accessibleIds is not null) query = query.Where(s => accessibleIds.Contains(s.Id));
        return await query
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<List<string>> GetServerNamesAsync(CancellationToken ct = default)
        => GetServerNamesAsync(null, ct);

    public async Task<List<(int Id, string Name)>> GetServerIdNamePairsAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking();
        if (accessibleIds is not null) query = query.Where(s => accessibleIds.Contains(s.Id));
        var rows = await query
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    // GetServersForAgentUpdateAsync moved to AgentUpdateRepository: it had no consumer outside the
    // agent-update flow, and serving it from here forced AgentUpdate to depend on Servers.

    public async Task<List<int>> GetProjectIdsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await ProjectIdsForServer(serverId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<int>> GetPipelineIdsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await PipelineIdsForServer(serverId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Project>> GetProjectsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Projects
            .Where(p => ProjectIdsForServer(serverId).Contains(p.Id))
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Pipeline>> GetPipelinesForServerAsync(int serverId, CancellationToken ct = default)
    {
        // S-TECH-P2WX: include Runs so the service can resolve LastRunStatus/LastRunAt - the
        // server-scoped list previously showed "-" for "Last run" even when runs existed.
        return await db.Pipelines
            .Where(p => PipelineIdsForServer(serverId).Contains(p.Id))
            .Include(p => p.Project)
            .Include(p => p.Runs)
            .AsNoTracking()
            .AsSplitQuery()
            .OrderBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<VariableLibrary>> GetVariableLibrariesForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.VariableLibraries
            .Where(v => (v.ProjectId != null && ProjectIdsForServer(serverId).Contains(v.ProjectId.Value))
                || (v.ProjectServerId != null && v.ProjectServer!.ServerId == serverId)
                || (v.EnvironmentId != null && v.Environment!.Servers.Any(link => link.ServerId == serverId))
                || (v.EnvironmentId != null && v.Environment!.LinkedProjectServers
                    .Any(link => link.ProjectServer.ServerId == serverId)))
            .Include(v => v.Project)
            .Include(v => v.Environment)
            .Include(v => v.ProjectServer)
            .Include(v => v.Entries)
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Vault>> GetVaultsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Vaults
            .Where(v => (v.ProjectId != null && ProjectIdsForServer(serverId).Contains(v.ProjectId.Value))
                || (v.ProjectServerId != null && v.ProjectServer!.ServerId == serverId)
                || (v.EnvironmentId != null && v.Environment!.Servers.Any(link => link.ServerId == serverId))
                || (v.EnvironmentId != null && v.Environment!.LinkedProjectServers
                    .Any(link => link.ProjectServer.ServerId == serverId)))
            .Include(v => v.Project)
            .Include(v => v.Environment)
            .Include(v => v.ProjectServer)
            .Include(v => v.Secrets)
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // GetReleasesForServerAsync moved to ReleaseRepository: the query is about releases, and keeping
    // it here forced Servers to depend on Pipelines to enrich each release with its source pipeline.


    private IQueryable<int> ProjectIdsForServer(int serverId)
    {
        var executedProjectIds = db.PipelineStepRuns
            .Where(psr => psr.ServerId == serverId)
            .Select(psr => psr.PipelineRun.Pipeline.ProjectId)
            .Where(pid => pid != null)
            .Select(pid => pid!.Value);
        var linkedProjectIds = db.ProjectServers
            .Where(projectServer => projectServer.ServerId == serverId)
            .Select(projectServer => projectServer.ProjectId);
        return executedProjectIds.Union(linkedProjectIds).Distinct();
    }

    private IQueryable<int> PipelineIdsForServer(int serverId)
    {
        return db.PipelineStepRuns
            .Where(psr => psr.ServerId == serverId)
            .Select(psr => psr.PipelineRun.PipelineId)
            .Distinct();
    }

    public async Task<ServiceType?> GetServiceTypeAsync(int serverId, string serviceName, CancellationToken ct = default)
    {
        return await db.ServiceInfos
            .Where(s => s.ServerId == serverId && s.Name == serviceName)
            .Select(s => (ServiceType?)s.Type)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(List<Project> Items, int Total)> GetProjectsForServerPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default)
    {
        var query = db.Projects
            .Where(project => ProjectIdsForServer(serverId).Contains(project.Id))
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(project =>
                EF.Functions.ILike(project.Name, pattern) ||
                EF.Functions.ILike(project.Description, pattern));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy?.Trim().ToLowerInvariant(), sortDescending) switch
        {
            ("status", false) => query.OrderBy(project => project.Status).ThenBy(project => project.Name).ThenBy(project => project.Id),
            ("status", true) => query.OrderByDescending(project => project.Status).ThenBy(project => project.Name).ThenBy(project => project.Id),
            ("createdat", false) => query.OrderBy(project => project.CreatedAt).ThenBy(project => project.Id),
            ("createdat", true) => query.OrderByDescending(project => project.CreatedAt).ThenBy(project => project.Id),
            (_, true) => query.OrderByDescending(project => project.Name).ThenBy(project => project.Id),
            _ => query.OrderBy(project => project.Name).ThenBy(project => project.Id)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        await ServerTaskRepositoryOperations.AddTaskAsync(db, task, ct).ConfigureAwait(false);
    }

    public async Task AddTasksAsync(IEnumerable<ServerTask> tasks, CancellationToken ct = default)
    {
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Tasks
            .Where(t => t.ServerId == serverId)
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public Task<int> CountActiveNonUpdateTasksAsync(int serverId, CancellationToken ct = default) =>
        db.Tasks.CountAsync(task =>
            task.ServerId == serverId
            && task.Operation != OperationKind.AgentSelfUpdate
            && (task.Status == TaskExecutionStatus.Pending
                || task.Status == TaskExecutionStatus.Assigned
                || task.Status == TaskExecutionStatus.Running), ct);

    public async Task<List<ServerDto>> GetServersForCompatibilityAsync(
        List<int>? accessibleIds,
        CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking();
        if (accessibleIds is not null)
            query = query.Where(server => accessibleIds.Contains(server.Id));

        var facts = await query.Select(server => new
        {
            server.Id,
            server.AgentVersion,
            server.AgentProtocolVersion,
            server.AgentCapabilitiesJson,
            server.LastHeartbeat,
            server.Status,
            server.PipelineRunnerEnabled,
            server.DeploymentTargetAvailable
        }).ToListAsync(ct).ConfigureAwait(false);
        return facts.Select(server => new ServerDto
        {
            Id = server.Id,
            AgentVersion = server.AgentVersion,
            AgentProtocolVersion = server.AgentProtocolVersion,
            AgentCapabilities = ServerDataMapper.DeserializeDiagnostics(server.AgentCapabilitiesJson),
            LastHeartbeat = server.LastHeartbeat,
            Status = server.Status,
            PipelineRunnerEnabled = server.PipelineRunnerEnabled,
            DeploymentTargetAvailable = server.DeploymentTargetAvailable
        }).ToList();
    }

    public async Task<(List<TaskLog> Items, int TotalCount)> GetLogsPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.TaskLogs
            .Where(l => l.Task.ServerId == serverId)
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }
}
