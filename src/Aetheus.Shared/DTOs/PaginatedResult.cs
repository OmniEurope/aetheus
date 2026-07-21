// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record PaginatedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public record PaginationRequest
{
    public const int MaxPageSize = PaginationDefaults.MaximumPageSize;
    public const int DefaultPageSize = PaginationDefaults.DefaultPageSize;

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    [StringLength(200)]
    public string? Search { get; init; }

    [StringLength(50)]
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }

    /// <summary>
    /// Returns clamped (Page, PageSize) values that are always safe to forward to a repository,
    /// even when the request was constructed manually outside the [ApiController] model-binding pipeline.
    /// </summary>
    public (int Page, int PageSize) Normalize()
    {
        var page = Page < 1 ? 1 : Page;
        var pageSize = PaginationDefaults.Clamp(PageSize);
        return (page, pageSize);
    }
}

public sealed record TaskPaginationRequest : PaginationRequest
{
    public TaskExecutionStatus? Status { get; init; }

    /// <summary>Optional single-server scope. When set, only that server's tasks are returned (still
    /// intersected with the caller's accessible servers). Drives the per-server tasks view so it
    /// shares the full feature set of the global /tasks page.</summary>
    public int? ServerId { get; init; }
}

public sealed record ProjectPaginationRequest : PaginationRequest
{
    public ProjectStatus? ProjectStatus { get; init; }
}

public sealed record PipelinePaginationRequest : PaginationRequest
{
    public PipelineTriggerType? TriggerType { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
    public int? ProjectId { get; init; }
    public int? ServerId { get; init; }
}

public sealed record ImportResultDto
{
    public int ImportedCount { get; init; }
}
