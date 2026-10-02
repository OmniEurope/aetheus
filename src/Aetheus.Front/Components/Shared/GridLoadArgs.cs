// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public sealed class GridLoadArgs
{
    public int? Skip { get; init; }
    public int? Top { get; init; }
    public string? OrderBy { get; init; }
    public string? Filter { get; init; }
    public IReadOnlyList<GridSortDescriptor>? Sorts { get; init; }
    public IReadOnlyList<GridFilterDescriptor>? Filters { get; init; }
}

public sealed record GridSortDescriptor(string Property, GridSortOrder SortOrder);

public enum GridSortOrder
{
    Ascending,
    Descending
}

/// <summary>One column's header filter. A multi-valued filter (<see cref="OmniDataGridFilterOperator.In"/>)
/// carries its values encoded, read them with <c>ColumnFilterValues</c>; a date range arrives as a
/// lower bound and an optional second, upper one, read them with <c>ColumnDateRange</c>.</summary>
public sealed record GridFilterDescriptor(
    string Property,
    object? FilterValue,
    OmniDataGridFilterOperator Operator,
    OmniDataGridFilterOperator? SecondOperator = null,
    string? SecondValue = null);
