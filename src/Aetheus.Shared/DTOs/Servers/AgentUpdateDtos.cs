// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

/// <summary>
/// Result of queuing an agent self-update for a single server. The actual update runs
/// asynchronously on the agent; progress and the final outcome surface through the
/// existing task-log / <c>TaskCompleted</c> SignalR channel.
/// </summary>
public sealed record AgentUpdateResponse
{
    /// <summary>Id of the queued self-update <c>ServerTask</c>.</summary>
    public int TaskId { get; init; }
}

/// <summary>
/// Result of queuing an agent self-update across every server the caller can administer.
/// </summary>
public sealed record AgentUpdateAllResponse
{
    /// <summary>Number of servers for which a self-update task was queued.</summary>
    public int QueuedCount { get; init; }

    /// <summary>Ids of the servers a self-update task was queued for.</summary>
    public List<int> ServerIds { get; init; } = [];
}

/// <summary>
/// Agent-side progress report for the self-update operation (item #3 of the plan). Emitted at
/// each phase transition of <c>AgentSelfUpdateOperationExecutor</c>. Backend persists nothing
/// (transient) and broadcasts it as <c>AgentUpdateProgress</c> on the server hub group.
/// </summary>
public sealed record AgentUpdateProgressDto
{
    public int ServerId { get; init; }
    public AgentUpdatePhase Phase { get; init; }

    /// <summary>0..100 progress percent. The agent emits a coarse value (10/20/50/60/80) so
    /// the UI doesn't need to compute byte-level progress while extraction happens.</summary>
    [Range(0, 100)]
    public int Percent { get; init; }

    /// <summary>Optional free-form context (e.g. "Downloaded 4500 KB", "tar exit code 2: …").
    /// Bounded so a misbehaving agent cannot flood the hub channel.</summary>
    [StringLength(500)]
    public string? Message { get; init; }
}

/// <summary>
/// Request body sent by the agent when it reports a progress phase. The matching server id is
/// derived from the agent's JWT (<c>ServerId</c> claim), so the request only carries the
/// payload - no caller-supplied server id to spoof.
/// </summary>
public sealed record AgentUpdateProgressReport
{
    public AgentUpdatePhase Phase { get; init; }

    [Range(0, 100)]
    public int Percent { get; init; }

    [StringLength(500)]
    public string? Message { get; init; }
}
