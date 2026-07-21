// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

// --- B-01: Work Items ---

public record WorkItemDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public WorkItemType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public WorkItemStatus Status { get; init; }
    public int? AssigneeUserId { get; init; }
    public string? AssigneeName { get; init; }
    public int? ParentId { get; init; }
    public int Priority { get; init; }
    public int Order { get; init; }
    public List<string> Tags { get; init; } = [];
    public int? LinkedPipelineRunId { get; init; }
    public string? ExternalId { get; init; }
    public string? ExternalUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record WorkItemDetailDto : WorkItemDto
{
    public List<WorkItemDto> Children { get; init; } = [];
}

public sealed record CreateWorkItemRequest
{
    public int ProjectId { get; init; }

    public WorkItemType Type { get; init; }

    [Required]
    [StringLength(300)]
    public string Title { get; init; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; init; }

    public WorkItemStatus Status { get; init; } = WorkItemStatus.New;
    public int? AssigneeUserId { get; init; }
    public int? ParentId { get; init; }
    public int Priority { get; init; }

    [MaxLength(20)]
    [MaxItemStringLength(50)]
    public List<string> Tags { get; init; } = [];
    public int? LinkedPipelineRunId { get; init; }

    [StringLength(200)]
    public string? ExternalId { get; init; }

    [StringLength(500)]
    public string? ExternalUrl { get; init; }
}

public sealed record UpdateWorkItemRequest
{
    public WorkItemType Type { get; init; }

    [Required]
    [StringLength(300)]
    public string Title { get; init; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; init; }

    public WorkItemStatus Status { get; init; }
    public int? AssigneeUserId { get; init; }
    public int? ParentId { get; init; }
    public int Priority { get; init; }
    public int Order { get; init; }

    [MaxLength(20)]
    [MaxItemStringLength(50)]
    public List<string> Tags { get; init; } = [];
    public int? LinkedPipelineRunId { get; init; }
}

public sealed record WorkItemPaginationRequest : PaginationRequest
{
    public int? ProjectId { get; init; }
    public WorkItemType? Type { get; init; }
    public WorkItemStatus? Status { get; init; }
    public int? AssigneeUserId { get; init; }
}

public sealed record WorkItemBoardColumn
{
    public WorkItemStatus Status { get; init; }
    public List<WorkItemDto> Items { get; init; } = [];
}

/// <summary>Lightweight card move (drag on the kanban board): only status + order change.</summary>
public sealed record MoveWorkItemRequest
{
    public WorkItemStatus Status { get; init; }
    public int Order { get; init; }
}
