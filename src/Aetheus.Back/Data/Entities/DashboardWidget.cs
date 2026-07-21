// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class DashboardWidget
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public DashboardWidgetType WidgetType { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Column { get; set; }
    public int Row { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public string? ConfigurationJson { get; set; }
    public bool IsVisible { get; set; } = true;

    // Navigation
    public Dashboard Dashboard { get; set; } = null!;
}
