// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

/// <summary>Read model for a monitored application (PLAN-001 phase 1: availability).</summary>
public sealed record MonitoredAppDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? EnvironmentId { get; init; }
    public string? EnvironmentName { get; init; }
    public int? ServerId { get; init; }
    public string? ServerName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ProbeUrl { get; init; }
    public int ProbeIntervalSeconds { get; init; }
    public int ProbeTimeoutSeconds { get; init; }
    public int ExpectedStatusCode { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
    public bool Enabled { get; init; }
    public AppHealthStatus CurrentStatus { get; init; }
    public DateTime? LastStatusChangeAt { get; init; }
    public DateTime? LastCheckedAt { get; init; }
    public int? LastResponseTimeMs { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Uptime ratio over the trailing 24 h, 7 d, 90 d in [0,1]; null when never sampled.</summary>
    public double? Uptime24h { get; init; }
    public double? Uptime7d { get; init; }
    public double? Uptime90d { get; init; }

    // OTLP ingestion (phase 2+): the key plaintext is never returned, only whether one is configured.
    public bool HasIngestKey { get; init; }
    public DateTime? IngestKeyCreatedAt { get; init; }
    public long IngestDroppedCount { get; init; }
    public DateTime? LastIngestAt { get; init; }
}

/// <summary>Plaintext OTLP ingestion key, shown exactly once at generation (never persisted in clear).</summary>
public sealed record IngestKeyResponse
{
    public string Key { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed record MetricPointDto
{
    public DateTime Timestamp { get; init; }
    public double Value { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? P95 { get; init; }
}

public sealed record MetricSeriesDto
{
    public string MetricName { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public IReadOnlyList<MetricPointDto> Points { get; init; } = [];
}

public sealed record AppMetricThresholdDto
{
    public int Id { get; init; }
    public int MonitoredAppId { get; init; }
    public string MetricName { get; init; } = string.Empty;
    public ComparisonOperator Operator { get; init; }
    public double Threshold { get; init; }
    public bool Enabled { get; init; }
    public bool IsBreached { get; init; }
    public DateTime? LastTriggeredAt { get; init; }
}

public sealed record CreateMetricThresholdRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string MetricName { get; set; } = string.Empty;

    [Required]
    public ComparisonOperator Operator { get; set; }

    public double Threshold { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed record AppLogEntryDto
{
    public DateTime Timestamp { get; init; }
    public int SeverityNumber { get; init; }
    public string? SeverityText { get; init; }
    public string Body { get; init; } = string.Empty;
    public string? AttributesJson { get; init; }
}

public sealed record AppErrorEventDto
{
    public int Id { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
    public string ExceptionType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? TopFrame { get; init; }
    public int OccurrenceCount { get; init; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; init; }
}

public sealed record CreateMonitoredAppRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int ProjectId { get; set; }

    public int? EnvironmentId { get; set; }

    /// <summary>Server hosting the app; null = app is off-fleet and probed directly by the backend (public URL only).</summary>
    public int? ServerId { get; set; }

    [StringLength(2048)]
    [Url]
    public string? ProbeUrl { get; set; }

    [Range(AppMonitoringDefaults.MinimumProbeIntervalSeconds, AppMonitoringDefaults.MaximumProbeIntervalSeconds)]
    public int ProbeIntervalSeconds { get; set; } = AppMonitoringDefaults.DefaultProbeIntervalSeconds;

    [Range(AppMonitoringDefaults.MinimumProbeTimeoutSeconds, AppMonitoringDefaults.MaximumProbeTimeoutSeconds)]
    public int ProbeTimeoutSeconds { get; set; } = AppMonitoringDefaults.DefaultProbeTimeoutSeconds;

    [Range(AppMonitoringDefaults.MinimumExpectedStatusCode, AppMonitoringDefaults.MaximumExpectedStatusCode)]
    public int ExpectedStatusCode { get; set; } = AppMonitoringDefaults.DefaultExpectedStatusCode;

    [Range(AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold)]
    public int FailureThreshold { get; set; } = AppMonitoringDefaults.DefaultFailureThreshold;

    [Range(AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold)]
    public int RecoveryThreshold { get; set; } = AppMonitoringDefaults.DefaultRecoveryThreshold;

    public bool Enabled { get; set; } = true;
}

public sealed record UpdateMonitoredAppRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public int? EnvironmentId { get; set; }

    public int? ServerId { get; set; }

    [StringLength(2048)]
    [Url]
    public string? ProbeUrl { get; set; }

    [Range(AppMonitoringDefaults.MinimumProbeIntervalSeconds, AppMonitoringDefaults.MaximumProbeIntervalSeconds)]
    public int ProbeIntervalSeconds { get; set; } = AppMonitoringDefaults.DefaultProbeIntervalSeconds;

    [Range(AppMonitoringDefaults.MinimumProbeTimeoutSeconds, AppMonitoringDefaults.MaximumProbeTimeoutSeconds)]
    public int ProbeTimeoutSeconds { get; set; } = AppMonitoringDefaults.DefaultProbeTimeoutSeconds;

    [Range(AppMonitoringDefaults.MinimumExpectedStatusCode, AppMonitoringDefaults.MaximumExpectedStatusCode)]
    public int ExpectedStatusCode { get; set; } = AppMonitoringDefaults.DefaultExpectedStatusCode;

    [Range(AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold)]
    public int FailureThreshold { get; set; } = AppMonitoringDefaults.DefaultFailureThreshold;

    [Range(AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold)]
    public int RecoveryThreshold { get; set; } = AppMonitoringDefaults.DefaultRecoveryThreshold;

    public bool Enabled { get; set; } = true;
}

/// <summary>A single availability data point (raw sample).</summary>
public sealed record AppHealthSampleDto
{
    public DateTime Timestamp { get; init; }
    public bool IsUp { get; init; }
    public int? ResponseTimeMs { get; init; }
    public int? StatusCode { get; init; }
    public string? Error { get; init; }
}

/// <summary>Probe configuration handed to an agent for the apps it hosts (pull model).</summary>
public sealed record AppProbeConfigDto
{
    public int MonitoredAppId { get; init; }
    public string ProbeUrl { get; init; } = string.Empty;
    public int ProbeIntervalSeconds { get; init; }
    public int ProbeTimeoutSeconds { get; init; }
    public int ExpectedStatusCode { get; init; }
}

/// <summary>A probe result reported by an agent (or produced by the backend prober).</summary>
public sealed record AppProbeResultDto
{
    [Required]
    public int MonitoredAppId { get; init; }

    public DateTime Timestamp { get; init; }

    public bool IsUp { get; init; }

    [Range(0, int.MaxValue)]
    public int? ResponseTimeMs { get; init; }

    [Range(0, 599)]
    public int? StatusCode { get; init; }

    [StringLength(500)]
    public string? Error { get; init; }
}

/// <summary>Global monitoring summary for the dashboard tile.</summary>
public sealed record AppMonitoringSummaryDto
{
    public int TotalCount { get; init; }
    public int UpCount { get; init; }
    public int DownCount { get; init; }
    public int DegradedCount { get; init; }
    public int UnknownCount { get; init; }
    /// <summary>Physical PostgreSQL bytes used by app telemetry tables; observable growth signal.</summary>
    public long TelemetryStorageBytes { get; init; }
    public IReadOnlyList<MonitoredAppStatusDto> Troubled { get; init; } = [];
}

/// <summary>Compact status line for an app in trouble (dashboard tile).</summary>
public sealed record MonitoredAppStatusDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public AppHealthStatus CurrentStatus { get; init; }
    public DateTime? LastStatusChangeAt { get; init; }
}
