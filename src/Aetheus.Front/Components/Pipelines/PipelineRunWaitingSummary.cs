// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// Why the run on screen is not advancing, gathered from the run itself and from every triggered
/// child already loaded by the page.
///
/// An orchestration is the case that needs this: the parent's own trigger step is Running, so the
/// parent never reports a wait, while the child three levels down is the one queuing for an offline
/// runner. Reading the parent alone shows a run that moves nothing and says nothing.
/// </summary>
public static class PipelineRunWaitingSummary
{
    /// <param name="Source">The child pipeline the wait comes from, or null for this run's own wait.</param>
    /// <param name="Reason">The scheduler's sentence, as recorded on the run.</param>
    /// <param name="Since">When that wait started, or null on a run predating the column.</param>
    public sealed record WaitView(string? Source, string Reason, DateTime? Since);

    public static IReadOnlyList<WaitView> Build(
        PipelineRunDto? run,
        IReadOnlyDictionary<int, PipelineRunDto>? children)
    {
        if (run is null) return [];

        var views = new List<WaitView>();
        if (run.WaitingReason is { Length: > 0 } own)
            views.Add(new WaitView(null, own, run.WaitingSince));

        // The cache the run page builds holds this run's descendants only, so no filtering by
        // lineage is needed here; ordering by id keeps the list stable across live refreshes.
        foreach (var child in (children?.Values ?? []).OrderBy(child => child.Id))
            if (child.Id != run.Id && child.WaitingReason is { Length: > 0 } reason)
                views.Add(new WaitView(child.PipelineName, reason, child.WaitingSince));

        return views;
    }
}
