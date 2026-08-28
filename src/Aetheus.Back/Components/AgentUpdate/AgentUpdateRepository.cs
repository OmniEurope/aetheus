// SPDX-License-Identifier: EUPL-1.2
using System.Data;
using System.Text.Json;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AgentUpdate;

internal sealed class AgentUpdateRepository(AppDbContext db, TimeProvider timeProvider)
    : IAgentUpdateRepository
{
    private const int UpdateTimeoutSeconds = 600;
    // --- Own-reads over Server -------------------------------------------------------------------
    // Two reads this module used to obtain by injecting IServerRepository, which put AgentUpdate and
    // Servers in the same cycle. The agent-update listing genuinely belongs here - it existed in the
    // server repository only because that is where it was written. FindServerAsync did not: it is a
    // shared load with many callers, so this is a deliberately local variant rather than a move,
    // which is what keeps the shared one where its other consumers expect it.

    /// <summary>
    /// Servers the agent-update view lists, optionally narrowed to what the caller may see.
    /// Moved out of ServerRepository, which had no other consumer for it.
    /// </summary>
    public async Task<List<Server>> GetServersForAgentUpdateAsync(
        List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsQueryable();
        if (accessibleIds is not null)
            query = query.Where(server => accessibleIds.Contains(server.Id));

        return await query
            .OrderBy(server => server.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tracked single-server load for the update flow. A local variant of the shared
    /// <c>ServerRepository.FindServerAsync</c>, not a move: that one has many other callers, and
    /// relocating it would have traded one cross-module dependency for several.
    /// </summary>
    public async Task<Server?> FindServerAsync(int id, CancellationToken ct = default) =>
        await db.Servers.FindAsync([id], ct).ConfigureAwait(false);

    /// <summary>
    /// The compatibility facts the update preview needs for a set of servers. A local variant, not a
    /// move: ServerService reads the same shape for its own views, so relocating it would only have
    /// pushed the dependency the other way.
    ///
    /// It calls the Servers mapper for the capabilities JSON. That is a pure static helper, not an
    /// injected collaborator, so it creates no dependency to invert - duplicating the deserialization
    /// would risk the two drifting on what a capability string means.
    /// </summary>
    public async Task<List<ServerDto>> GetServersForCompatibilityAsync(
        List<int>? accessibleIds, CancellationToken ct = default)
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
            AgentCapabilities = Servers.ServerDataMapper.DeserializeDiagnostics(server.AgentCapabilitiesJson),
            LastHeartbeat = server.LastHeartbeat,
            Status = server.Status,
            PipelineRunnerEnabled = server.PipelineRunnerEnabled,
            DeploymentTargetAvailable = server.DeploymentTargetAvailable
        }).ToList();
    }

    /// <summary>
    /// Work in flight on a server that is not itself an agent update - what makes it too busy to be
    /// updated now. A local variant for the same reason as the compatibility read above.
    /// </summary>
    public Task<int> CountActiveNonUpdateTasksAsync(int serverId, CancellationToken ct = default) =>
        db.Tasks.CountAsync(task =>
            task.ServerId == serverId
            && task.Operation != OperationKind.AgentSelfUpdate
            && (task.Status == TaskExecutionStatus.Pending
                || task.Status == TaskExecutionStatus.Assigned
                || task.Status == TaskExecutionStatus.Running), ct);

    /// <summary>
    /// The same question asked for a whole fleet in one round trip. The preview dialog used to call the
    /// single-server count per candidate server, so a fleet of 200 with 150 outdated agents issued 150
    /// queries to render one dialog. Servers with no active task are simply absent from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> CountActiveNonUpdateTasksAsync(
        IReadOnlyCollection<int> serverIds, CancellationToken ct = default)
    {
        if (serverIds.Count == 0) return new Dictionary<int, int>();

        var counts = await db.Tasks
            .Where(task => serverIds.Contains(task.ServerId)
                && task.Operation != OperationKind.AgentSelfUpdate
                && (task.Status == TaskExecutionStatus.Pending
                    || task.Status == TaskExecutionStatus.Assigned
                    || task.Status == TaskExecutionStatus.Running))
            .GroupBy(task => task.ServerId)
            .Select(group => new { ServerId = group.Key, Count = group.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        return counts.ToDictionary(entry => entry.ServerId, entry => entry.Count);
    }

    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(5);

    public async Task<(AgentUpdateRequest Request, bool Created)> ReserveAsync(
        Server server,
        AgentReleaseManifestDto release,
        string requestedBy,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false)
            : null;

        // Serialize reservations by updating the durable server row inside the transaction.
        // The self-assignment is intentionally value-neutral but still acquires the provider row
        // lock. Read Committed is sufficient because every contender takes this lock before reading
        // active requests; Serializable would turn the expected contention into SQLSTATE 40001.
        var reservedServer = server;
        if (db.Database.IsRelational())
        {
            await db.Servers
                .Where(item => item.Id == server.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.AgentUpdateReserved,
                    item => item.AgentUpdateReserved), ct)
                .ConfigureAwait(false);
        }

        var existing = await db.AgentUpdateRequests
            .FirstOrDefaultAsync(request =>
                request.ServerId == server.Id
                && request.TargetVersion == release.SoftwareVersion
                && request.IsActive, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (existing, false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = new AgentUpdateRequest
        {
            ServerId = server.Id,
            TargetVersion = release.SoftwareVersion,
            ObservedVersion = reservedServer.AgentVersion,
            ObservedProtocolVersion = reservedServer.AgentProtocolVersion,
            ObservedSessionId = reservedServer.AgentSessionId,
            RequestedBy = requestedBy,
            RequestedAt = now,
            Status = AgentUpdateRequestStatus.WaitingForIdle,
            IsActive = true,
            ExpectedCapabilitiesJson = JsonSerializer.Serialize(
                BuildExpectedCapabilities(reservedServer))
        };
        reservedServer.AgentUpdateReserved = true;
        reservedServer.AgentUpdateReservedAt = now;
        db.AgentUpdateRequests.Add(request);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (request, true);
    }

    public async Task<(AgentUpdateRequest Request, ServerTask? CreatedTask, int BlockingTaskCount)> TryQueueAsync(
        int requestId,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false)
            : null;
        var request = await db.AgentUpdateRequests
            .Include(item => item.Server)
            .FirstAsync(item => item.Id == requestId, ct)
            .ConfigureAwait(false);
        if (!request.IsActive || request.TaskId is not null)
        {
            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (request, null, 0);
        }

        // Pending work has not started and is already fenced behind the update reservation. Counting
        // it as active creates a circular wait when the old agent cannot claim that exact operation:
        // the task waits for a newer capability while the self-update waits for the task. Queue the
        // exclusive, priority self-update ahead of pending work; only an assigned/running operation
        // must finish before the agent process can safely restart.
        var blocking = await db.Tasks.CountAsync(task =>
            task.ServerId == request.ServerId
            && task.Operation != OperationKind.AgentSelfUpdate
            && (request.Server.AgentUpdateReservedAt == null
                || task.CreatedAt <= request.Server.AgentUpdateReservedAt)
            && (task.Status == TaskExecutionStatus.Assigned
                || task.Status == TaskExecutionStatus.Running), ct).ConfigureAwait(false);
        if (blocking > 0)
        {
            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (request, null, blocking);
        }

        var task = new ServerTask
        {
            ServerId = request.ServerId,
            Name = $"Agent self-update to {request.TargetVersion}",
            Command = string.Empty,
            Executor = ExecutorType.Operation,
            Operation = OperationKind.AgentSelfUpdate,
            Status = TaskExecutionStatus.Pending,
            TimeoutSeconds = UpdateTimeoutSeconds,
            EnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["AETHEUS_AGENT_TARGET_VERSION"] = request.TargetVersion
            }),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        request.TaskId = task.Id;
        request.Status = AgentUpdateRequestStatus.Queued;
        request.StartedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (request, task, 0);
    }

    public Task<List<int>> GetWaitingRequestIdsAsync(CancellationToken ct) =>
        db.AgentUpdateRequests.AsNoTracking()
            .Where(request => request.IsActive
                && request.Status == AgentUpdateRequestStatus.WaitingForIdle)
            .OrderBy(request => request.RequestedAt)
            .Select(request => request.Id)
            .ToListAsync(ct);

    public Task<AgentUpdateRequest?> FindActiveByServerAsync(int serverId, CancellationToken ct) =>
        db.AgentUpdateRequests.AsNoTracking()
            .Where(request => request.ServerId == serverId && request.IsActive)
            .OrderByDescending(request => request.RequestedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<AgentUpdateRequest?> MarkProgressAsync(
        int serverId,
        AgentUpdateProgressDto progress,
        CancellationToken ct)
    {
        var request = await db.AgentUpdateRequests
            .FirstOrDefaultAsync(item => item.ServerId == serverId && item.IsActive, ct)
            .ConfigureAwait(false);
        if (request is null) return null;

        request.Status = progress.Phase switch
        {
            AgentUpdatePhase.Downloading => AgentUpdateRequestStatus.Downloading,
            AgentUpdatePhase.Downloaded or AgentUpdatePhase.Extracting
                => AgentUpdateRequestStatus.Staging,
            AgentUpdatePhase.LaunchingUpdater or AgentUpdatePhase.AgentOffline or AgentUpdatePhase.Done
                => AgentUpdateRequestStatus.Handoff,
            AgentUpdatePhase.Failed => AgentUpdateRequestStatus.Failed,
            _ => request.Status
        };
        if (request.Status == AgentUpdateRequestStatus.Handoff && request.HandoffAt is null)
        {
            request.HandoffAt = timeProvider.GetUtcNow().UtcDateTime;
            request.ConfirmationDeadline = request.HandoffAt.Value.Add(ConfirmationTimeout);
        }
        if (request.Status == AgentUpdateRequestStatus.Failed)
        {
            request.IsActive = false;
            request.FailureCode = "agent-update-failed";
            request.FailureDiagnostic = progress.Message;
            var server = await db.Servers.FirstAsync(item => item.Id == serverId, ct).ConfigureAwait(false);
            server.AgentUpdateReserved = false;
            server.AgentUpdateReservedAt = null;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request;
    }

    public async Task<AgentUpdateRequest?> ConfirmFromHeartbeatAsync(
        int serverId,
        ServerHeartbeatDto heartbeat,
        CancellationToken ct)
    {
        var request = await db.AgentUpdateRequests
            .Include(item => item.Server)
            .Include(item => item.Task)
            .FirstOrDefaultAsync(item => item.ServerId == serverId && item.IsActive, ct)
            .ConfigureAwait(false);
        if (request is null || request.Status != AgentUpdateRequestStatus.Handoff)
            return null;

        var sessionChanged = !string.IsNullOrWhiteSpace(heartbeat.AgentSessionId)
            && !string.Equals(heartbeat.AgentSessionId, request.ObservedSessionId, StringComparison.Ordinal);
        if (!sessionChanged) return null;

        // Linux hands off to a root-owned systemd worker after the agent exits. systemd can restart
        // the old binary once before that worker swaps the installation, so keep waiting until the
        // target version confirms or the existing confirmation deadline expires.
        if (string.Equals(heartbeat.AgentVersion, request.ObservedVersion, StringComparison.Ordinal))
            return null;

        if (!string.Equals(heartbeat.AgentVersion, request.TargetVersion, StringComparison.Ordinal))
            return await FailAsync(request, "wrong-version",
                $"Expected {request.TargetVersion}, received {heartbeat.AgentVersion ?? "missing"}.", ct)
                .ConfigureAwait(false);
        if (heartbeat.AgentProtocolVersion is not { } protocol || !AgentProtocol.IsSupported(protocol))
            return await FailAsync(request, "unsupported-protocol",
                $"Received unsupported protocol {heartbeat.AgentProtocolVersion?.ToString() ?? "missing"}.", ct)
                .ConfigureAwait(false);

        var expected = JsonSerializer.Deserialize<List<string>>(request.ExpectedCapabilitiesJson) ?? [];
        var reported = heartbeat.AgentCapabilities ?? [];
        var missing = expected.Except(reported, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            return await FailAsync(request, "missing-capabilities",
                $"Missing expected capabilities: {string.Join(", ", missing)}.", ct).ConfigureAwait(false);

        request.Status = AgentUpdateRequestStatus.Confirmed;
        request.IsActive = false;
        request.ConfirmedAt = timeProvider.GetUtcNow().UtcDateTime;
        request.ConfirmedSessionId = heartbeat.AgentSessionId;
        request.Server.AgentUpdateReserved = false;
        request.Server.AgentUpdateReservedAt = null;
        CompleteUpdateTask(
            request,
            TaskExecutionStatus.Success,
            request.ConfirmedAt.Value,
            exitCode: 0);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request;
    }

    public async Task<List<AgentUpdateRequest>> FailExpiredAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updateDeadline = now.AddSeconds(-UpdateTimeoutSeconds);
        var expired = await db.AgentUpdateRequests
            .Include(request => request.Server)
            .Include(request => request.Task)
            .Where(request => request.IsActive
                && ((request.Status == AgentUpdateRequestStatus.Handoff
                        && request.ConfirmationDeadline != null
                        && request.ConfirmationDeadline < now)
                    || (request.Status != AgentUpdateRequestStatus.WaitingForIdle
                        && request.Status != AgentUpdateRequestStatus.Handoff
                        && request.StartedAt != null
                        && request.StartedAt < updateDeadline)
                    // Once the old agent has handed off, its task can be marked terminal by a
                    // concurrent generic watchdog as the process session changes. The heartbeat
                    // contract, not that stale task state, owns the outcome during confirmation.
                    || (request.Status != AgentUpdateRequestStatus.Handoff
                        && request.Task != null
                        && (request.Task.Status == TaskExecutionStatus.Failed
                            || request.Task.Status == TaskExecutionStatus.Timeout
                            || request.Task.Status == TaskExecutionStatus.Cancelled))))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var request in expired)
        {
            request.Status = AgentUpdateRequestStatus.Failed;
            request.IsActive = false;
            var confirmationTimedOut = request.HandoffAt is not null;
            request.FailureCode = confirmationTimedOut
                ? "confirmation-timeout"
                : "update-task-timeout";
            request.FailureDiagnostic = confirmationTimedOut
                ? "No confirming heartbeat arrived before the deadline."
                : "The update task did not reach handoff before its execution deadline.";
            if (request.Task is
                {
                    Status: TaskExecutionStatus.Pending
                    or TaskExecutionStatus.Assigned
                    or TaskExecutionStatus.Running
                })
            {
                CompleteUpdateTask(
                    request,
                    confirmationTimedOut
                        ? TaskExecutionStatus.Timeout
                        : TaskExecutionStatus.Cancelled,
                    now,
                    exitCode: -1);
            }
            request.Server.AgentUpdateReserved = false;
            request.Server.AgentUpdateReservedAt = null;
        }
        if (expired.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired;
    }

    private async Task<AgentUpdateRequest> FailAsync(
        AgentUpdateRequest request,
        string code,
        string diagnostic,
        CancellationToken ct)
    {
        request.Status = AgentUpdateRequestStatus.Failed;
        request.IsActive = false;
        request.FailureCode = code;
        request.FailureDiagnostic = diagnostic;
        request.Server.AgentUpdateReserved = false;
        request.Server.AgentUpdateReservedAt = null;
        if (request.Task is
            {
                Status: TaskExecutionStatus.Pending
                or TaskExecutionStatus.Assigned
                or TaskExecutionStatus.Running
            })
        {
            CompleteUpdateTask(
                request,
                TaskExecutionStatus.Failed,
                timeProvider.GetUtcNow().UtcDateTime,
                exitCode: -1);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request;
    }

    private static void CompleteUpdateTask(
        AgentUpdateRequest request,
        TaskExecutionStatus status,
        DateTime completedAt,
        int exitCode)
    {
        if (request.Task is not { } task) return;
        task.Status = status;
        task.ExitCode = exitCode;
        task.CompletedAt = completedAt;
        task.EnvironmentVariables = TaskEnvProtection.EmptyEnv;
    }

    private static List<string> BuildExpectedCapabilities(Server server)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            AgentCapabilities.SelfUpdate,
            AgentCapabilities.ShellExecution
        };
        if (server.PipelineRunnerEnabled)
            expected.Add(AgentCapabilities.PipelineBuild);
        if (server.DeploymentTargetAvailable)
            expected.Add(AgentCapabilities.Deployment);
        return expected.Order(StringComparer.Ordinal).ToList();
    }
}
