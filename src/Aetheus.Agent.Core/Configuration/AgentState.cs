// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Configuration;

public sealed class AgentState(TimeProvider timeProvider)
{
    public AgentState() : this(TimeProvider.System)
    {
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _taskFencingTokens = new();
    /// <summary>
    /// Identifies this exact agent process lifetime. A restart creates a new value, allowing the
    /// backend to distinguish genuinely orphaned work from a long-running process that is still alive.
    /// </summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public int? ServerId { get; set; }
    public string? BearerToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public bool IsEnrolled => ServerId.HasValue && !string.IsNullOrEmpty(BearerToken);
    public bool IsTokenExpired =>
        TokenExpiresAt.HasValue && timeProvider.GetUtcNow().UtcDateTime >= TokenExpiresAt.Value;

    public void TrackTaskLease(int taskId, long fencingToken)
    {
        if (fencingToken > 0)
            _taskFencingTokens[taskId] = fencingToken;
    }

    public AgentTaskLeaseRequest GetTaskLease(int taskId) => new()
    {
        AgentSessionId = SessionId,
        AgentSessionFencingToken = _taskFencingTokens.TryGetValue(taskId, out var token) ? token : 0
    };

    public long? GetTaskFencingToken(int taskId) =>
        _taskFencingTokens.TryGetValue(taskId, out var token) ? token : null;

    public void ReleaseTaskLease(int taskId) => _taskFencingTokens.TryRemove(taskId, out _);

    public void ClearSensitiveData()
    {
        BearerToken = null;
        TokenExpiresAt = null;
        _taskFencingTokens.Clear();
    }
}
