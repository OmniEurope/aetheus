// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Logs;

public sealed record TaskLogDto
{
    public int Id { get; init; }
    public int TaskId { get; init; }
    public TaskLogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}

public sealed record AppendLogRequest
{
    public int TaskId { get; init; }
    [StringLength(64)]
    public string? AgentSessionId { get; init; }
    public long? AgentSessionFencingToken { get; init; }

    public TaskLogLevel Level { get; init; }

    [StringLength(50000)]
    public string Message { get; init; } = string.Empty;
}
