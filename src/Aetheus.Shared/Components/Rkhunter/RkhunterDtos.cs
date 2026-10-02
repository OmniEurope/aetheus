// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Rkhunter;

public sealed record RkhunterDataDto
{
    public bool IsInstalled { get; init; }
    [StringLength(100)]
    public string Version { get; init; } = string.Empty;
    [StringLength(100)]
    public string DatabaseVersion { get; init; } = string.Empty;
    public DateTime LastScanTime { get; init; }
    [StringLength(100)]
    public string LastScanStatus { get; init; } = string.Empty;
    public int WarningCount { get; init; }
    public DateTime DatabaseLastUpdated { get; init; }
    [StringLength(120)]
    public string? ScanScheduleCron { get; init; }
}

public sealed record RkhunterWarningDto
{
    public int Id { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public DateTime FoundAt { get; init; }
    public bool IsArchived { get; init; }
}

public sealed record RkhunterActionRequest
{
    [Required]
    public RkhunterAction Action { get; init; }
}

public sealed record RkhunterSetupRequest
{
    [StringLength(200)]
    public string MailOnWarning { get; init; } = string.Empty;
}

public sealed record RkhunterLogRequest
{
    [Range(1, 5000)]
    public int Lines { get; init; } = 200;
}

public sealed record RkhunterScheduleRequest
{
    [StringLength(100)]
    public string? CronExpression { get; init; }
}

public sealed record RkhunterScanResultDto
{
    public int Id { get; init; }
    public DateTime ScanTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public int WarningCount { get; init; }
    public string Summary { get; init; } = string.Empty;
}
