// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>Turns what the runs grid asks for into the query the API takes.
/// <para>The grid is server-paged, so the sort and the column filters have to travel with the page
/// request. Reading only the page, as this page used to, is what made the header affordances
/// decorative: the filter popups opened, the sort arrows moved, and the same rows came back in the
/// same order.</para>
/// <para>Recette R-210 / R-224: every header filter travels as a generic column filter (the status as a
/// checkable list, the dates as ranges), which the API checks against the runs grid's own column map.
/// The typed status / branch / commit parameters are no longer sent: a checkable list does not fit a
/// single status, and sending both would narrow twice.</para></summary>
internal static class PipelineRunsGridQuery
{
    public static PipelineRunPaginationRequest From(GridLoadArgs args)
    {
        var (sortBy, descending) = args.ToSortRequest(
            nameof(PipelineRunDto.StartedAt), fallbackDescending: true);

        return new PipelineRunPaginationRequest
        {
            SortBy = sortBy,
            SortDescending = descending,
            Filters = args.ToApiFilters()
        };
    }
}
