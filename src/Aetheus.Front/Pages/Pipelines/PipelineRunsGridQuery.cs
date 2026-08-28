// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>Turns what the runs grid asks for into the query the API takes.
/// <para>The grid is server-paged, so the sort and the column filters have to travel with the page
/// request. Reading only the page, as this page used to, is what made the header affordances
/// decorative: the filter popups opened, the sort arrows moved, and the same rows came back in the
/// same order.</para></summary>
internal static class PipelineRunsGridQuery
{
    public static PipelineRunPaginationRequest From(LoadDataArgs args)
    {
        var (sortBy, descending) = args.ToSortRequest(
            nameof(PipelineRunDto.StartedAt), fallbackDescending: true);

        return new PipelineRunPaginationRequest
        {
            SortBy = sortBy,
            SortDescending = descending,
            Status = args.ColumnFilter<PipelineStatus>(nameof(PipelineRunDto.Status)),
            BranchName = args.ColumnFilter(nameof(PipelineRunDto.BranchName)),
            CommitHash = args.ColumnFilter(nameof(PipelineRunDto.CommitHash))
        };
    }
}
