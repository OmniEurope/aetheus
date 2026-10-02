// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

internal static class OmniDataGridLoadRequestExtensions
{
    public static GridLoadArgs ToGridLoadArgs(this OmniDataGridLoadRequest request)
    {
        var sorts = request.Sorts.Select(sort => new GridSortDescriptor(
            sort.Key,
            sort.Descending ? GridSortOrder.Descending : GridSortOrder.Ascending)).ToArray();
        var filters = request.Filters.Select(filter => new GridFilterDescriptor(
            filter.Key,
            filter.Value,
            filter.Operator,
            filter.SecondOperator,
            filter.SecondValue)).ToArray();

        return new GridLoadArgs
        {
            Skip = request.Skip,
            Top = request.Top,
            OrderBy = request.Sorts.Count == 0 ? null : string.Join(",", request.Sorts.Select(sort =>
                $"{sort.Key}{(sort.Descending ? " desc" : string.Empty)}")),
            Sorts = sorts,
            Filters = filters
        };
    }
}
