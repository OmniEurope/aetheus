// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record ServiceInfoDto
{
    public string Name { get; init; } = string.Empty;
    public ServiceType Type { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public bool IsManageable { get; init; }
    public bool IsInstalled { get; init; } = true;
}

public sealed record ServiceActionRequest
{
    [Required]
    public ServiceAction Action { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-@]{0,63}$",
        ErrorMessage = "Service name contains invalid characters.")]
    public string ServiceName { get; init; } = string.Empty;
}

public sealed record ServiceInstallRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-@]{0,63}$",
        ErrorMessage = "Service name contains invalid characters.")]
    public string ServiceName { get; init; } = string.Empty;
}

public sealed record ServiceLogsRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string ServiceName { get; init; } = string.Empty;

    [Range(10, 1000)]
    public int Lines { get; init; } = 100;

    public bool Follow { get; init; }
}

public sealed record ServiceLogsResponse
{
    public int TaskId { get; init; }
}

/// <summary>
/// Returned by the service action / install / uninstall endpoints so the caller can correlate the
/// resulting <c>TaskCompleted</c> notification by id rather than by task-name suffix (which collides
/// when two operations target the same service).
/// </summary>
public sealed record ServiceTaskResponse
{
    public int TaskId { get; init; }
}

public sealed record ServerMetricDto
{
    public int ServerId { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryUsedMb { get; init; }
    public double MemoryTotalMb { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskTotalGb { get; init; }
    public bool BuildCacheAvailable { get; init; }
    public bool DockerInventoryAvailable { get; init; }
    public long BuildCacheBytes { get; init; }
    public long BuildCacheReclaimableBytes { get; init; }
    public long DockerImagesBytes { get; init; }
    public long DockerContainersBytes { get; init; }
    public long DockerVolumesBytes { get; init; }
    public long AgentWorkDirectoryBytes { get; init; }
    public long AgentInstallDirectoryBytes { get; init; }
    public long NuGetCacheBytes { get; init; }
    public long JournalBytes { get; init; }
    public bool StorageMaintenanceDryRun { get; init; }
    public bool DeploymentOnly { get; init; }
    public bool BuildActive { get; init; }
    public DateTime? LastBuildAttemptAtUtc { get; init; }
    public DateTime Timestamp { get; init; }
}

public sealed record AlertRuleDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? ServerId { get; init; }
    public string? ServerName { get; init; }
    public MetricType Metric { get; init; }
    public ComparisonOperator Operator { get; init; }
    public double Threshold { get; init; }
    public int SustainedSeconds { get; init; }
    public AlertSeverity Severity { get; init; }
    public bool IsEnabled { get; init; }
    public int? NotificationChannelId { get; init; }
    public string? NotificationChannelName { get; init; }
    public DateTime? LastTriggeredAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateAlertRuleRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public int? ServerId { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    public MetricType Metric { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    public ComparisonOperator Operator { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    [Range(0, double.MaxValue)]
    public double Threshold { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Range(10, 3600)]
    public int SustainedSeconds { get; set; } = 60; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public int? NotificationChannelId { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public bool IsEnabled { get; set; } = true; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)
}

public sealed record UpdateAlertRuleRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public int? ServerId { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    public MetricType Metric { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    public ComparisonOperator Operator { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Required]
    [Range(0, double.MaxValue)]
    public double Threshold { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    [Range(10, 3600)]
    public int SustainedSeconds { get; set; } = 60; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public bool IsEnabled { get; set; } = true; // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)

    public int? NotificationChannelId { get; set; } // audit: kept set; (mutated post-construction by Pages/Alerts/Alerts.razor @bind)
}

public sealed record EnvironmentCheckDto
{
    public int Id { get; init; }
    public int EnvironmentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public EnvironmentCheckType Type { get; init; }
    public string? Configuration { get; init; }
    public bool IsRequired { get; init; }
    public int TimeoutSeconds { get; init; }
    public DateTime CreatedAt { get; init; }
}
