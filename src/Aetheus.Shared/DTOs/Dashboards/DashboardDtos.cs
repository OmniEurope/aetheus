// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

// --- P-33: Customizable Dashboards ---

public sealed record DashboardDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public List<DashboardWidgetDto> Widgets { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public Guid RowVersion { get; init; }
}

public sealed record DashboardWidgetDto
{
    public int Id { get; init; }
    public int DashboardId { get; init; }
    public DashboardWidgetType WidgetType { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Column { get; init; }
    public int Row { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? ConfigurationJson { get; init; }
    public bool IsVisible { get; init; }
}

public sealed record CreateDashboardRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
    [MaxLength(50)]
    public List<CreateDashboardWidgetRequest> Widgets { get; init; } = [];
}

public sealed record UpdateDashboardRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
    public Guid RowVersion { get; init; }
    [MaxLength(50)]
    public List<CreateDashboardWidgetRequest> Widgets { get; init; } = [];
}

public sealed record CreateDashboardWidgetRequest
{
    public DashboardWidgetType WidgetType { get; init; }

    [Required]
    [StringLength(100)]
    public string Title { get; init; } = string.Empty;

    [Range(0, 100)]
    public int Column { get; init; }
    [Range(0, 100)]
    public int Row { get; init; }
    [Range(0, 100)]
    public int Width { get; init; } = 1;
    [Range(0, 100)]
    public int Height { get; init; } = 1;

    [StringLength(2000)]
    public string? ConfigurationJson { get; init; }

    public bool IsVisible { get; init; } = true;
}
