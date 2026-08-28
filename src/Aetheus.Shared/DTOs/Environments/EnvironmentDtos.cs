// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record EnvironmentDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EnvironmentType Type { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public bool RequireApproval { get; init; }
    public int ApprovalTimeoutMinutes { get; init; }
    public string? ApprovalInstructions { get; init; }
    public bool DastEnabled { get; init; }
    public bool DastIsEphemeral { get; init; }
    public bool DastContainsRealData { get; init; }
    public string DastAllowedHosts { get; init; } = string.Empty;
    public List<EnvironmentServerDto> Servers { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record EnvironmentServerDto
{
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public ServerStatus ServerStatus { get; init; }
}

public abstract record EnvironmentRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    public EnvironmentType Type { get; init; } = EnvironmentType.Development;

    public int? ProjectId { get; init; }

    public bool RequireApproval { get; init; }

    [Range(1, 10080)]
    public int ApprovalTimeoutMinutes { get; init; } = 1440;

    [StringLength(1000)]
    public string? ApprovalInstructions { get; init; }

    public bool DastEnabled { get; init; }
    public bool DastIsEphemeral { get; init; }
    public bool DastContainsRealData { get; init; }
    [StringLength(2000)] public string DastAllowedHosts { get; init; } = string.Empty;

    [MaxLength(500)]
    public List<int> ServerIds { get; init; } = [];
}

public sealed record CreateEnvironmentRequest : EnvironmentRequest
{
    [Range(1, int.MaxValue)]
    public int? SourceEnvironmentId { get; init; }
}

public sealed record DuplicateEnvironmentRequest
{
    public int? TargetProjectId { get; init; }
}

public sealed record UpdateEnvironmentRequest : EnvironmentRequest;
