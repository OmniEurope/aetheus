// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record PipelineApprovalDto
{
    public int Id { get; init; }
    public int PipelineRunId { get; init; }
    public string StageName { get; init; } = string.Empty;
    public int EnvironmentId { get; init; }
    public string EnvironmentName { get; init; } = string.Empty;
    public ApprovalStatus Status { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public int? ResolvedByUserId { get; init; }
    public string? ResolvedByUsername { get; init; }
    public string? Comments { get; init; }
}

public sealed record ApprovalDecisionRequest
{
    [Required]
    public ApprovalStatus Decision { get; init; }

    [StringLength(500)]
    public string? Comments { get; init; }
}
