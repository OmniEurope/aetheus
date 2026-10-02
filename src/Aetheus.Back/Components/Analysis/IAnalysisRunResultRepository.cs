// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

public interface IAnalysisRunResultRepository
{
    /// <summary>The run's project, whether it finished, and the stamp of its result; null when the run
    /// does not exist or no project owns it. One statement of indexed scalar lookups.</summary>
    Task<AnalysisRunResultStamp?> GetResultStampAsync(int runId, CancellationToken ct = default);

    /// <summary>One page of the findings the runs observed, filtered and sorted as the request says (most
    /// severe first by default), with the counts of the whole set.</summary>
    Task<AnalysisRunFindingsPageDto> GetFindingsPageAsync(
        IReadOnlyCollection<int> runIds, AnalysisRunFindingsRequest request, int page, int pageSize, CancellationToken ct = default);
}
