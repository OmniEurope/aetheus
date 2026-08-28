// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineArtifactService
{
    Task<List<PipelineArtifactDto>> GetArtifactsAsync(int runId, CancellationToken ct = default);
    Task<PipelineArtifactDto?> PublishArtifactAsync(int runId, PublishArtifactRequest request, CancellationToken ct = default);
    Task<List<PipelineTestResultDto>> GetTestResultsAsync(int runId, CancellationToken ct = default);
    Task<PipelineTestResultSummaryDto?> PublishTestResultsAsync(int runId, PublishTestResultsRequest request, CancellationToken ct = default);
    Task<PipelineCoverageSummaryDto?> GetCoverageSummaryAsync(int runId, CancellationToken ct = default);
    Task<PaginatedResult<CoverageAssemblyDto>> GetCoverageAssembliesAsync(
        int runId, PaginationRequest request, CancellationToken ct = default);
    Task<List<CoverageTrendPointDto>> GetCoverageTrendAsync(int runId, int take, CancellationToken ct = default);
    Task<List<ComplexityTrendPointDto>> GetComplexityTrendAsync(int runId, int take, CancellationToken ct = default);
    Task<ProjectQualityTrendDto> GetProjectQualityTrendAsync(int projectId, int take, CancellationToken ct = default);
    Task<PipelineCoverageSummaryDto?> PublishCoverageAsync(int runId, PublishCoverageRequest request, CancellationToken ct = default);
    Task<PipelineLintSummaryDto?> GetLintSummaryAsync(int runId, CancellationToken ct = default);
    Task<PipelineLintSummaryDto?> PublishLintAsync(int runId, PublishLintRequest request, CancellationToken ct = default);
    Task<bool> PublishComplexityAsync(int runId, PublishComplexityRequest request, CancellationToken ct = default);
}
