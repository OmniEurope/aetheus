// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class WorkItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public WorkItemType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.New;
    public int? AssigneeUserId { get; set; }
    public int? ParentId { get; set; }
    public int Priority { get; set; }
    public int Order { get; set; }
    public string Tags { get; set; } = string.Empty;
    public int? LinkedPipelineRunId { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public User? Assignee { get; set; }
    public WorkItem? Parent { get; set; }
    public List<WorkItem> Children { get; set; } = [];
    public PipelineRun? LinkedPipelineRun { get; set; }
}
