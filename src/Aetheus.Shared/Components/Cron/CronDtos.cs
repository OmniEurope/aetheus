// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Cron;

// --- Aggregate heartbeat data ---

public sealed record CronDataDto
{
    public bool IsInstalled { get; init; }
    [MaxLength(2048)]
    public List<CronJobDto> Jobs { get; init; } = [];
}

// --- Cron job info ---

public sealed record CronJobDto
{
    [StringLength(64)]
    public string Id { get; init; } = string.Empty;
    [StringLength(64)]
    public string User { get; init; } = string.Empty;
    [StringLength(100)]
    public string Schedule { get; init; } = string.Empty;
    [StringLength(1000)]
    public string Command { get; init; } = string.Empty;
    [StringLength(200)]
    public string Source { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
}

// --- Create/Update request ---

public sealed record CronJobSaveRequest
{
    [StringLength(64)]
    public string? Id { get; init; }

    [Required]
    [StringLength(32)]
    public string User { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Schedule { get; init; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Command { get; init; } = string.Empty;
}

// --- Delete request ---

public sealed record CronJobDeleteRequest
{
    [Required]
    [StringLength(64)]
    public string Id { get; init; } = string.Empty;

    [Required]
    [StringLength(32)]
    public string User { get; init; } = string.Empty;
}
