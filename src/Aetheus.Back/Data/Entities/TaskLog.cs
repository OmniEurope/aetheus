// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class TaskLog
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public TaskLogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OriginalMessage { get; set; }
    public DateTime Timestamp { get; set; }

    // Navigation
    public ServerTask Task { get; set; } = null!;
}
