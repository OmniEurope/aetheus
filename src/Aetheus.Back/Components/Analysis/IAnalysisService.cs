// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

public interface IAnalysisService
{
    Task<AnalysisReportDto> PublishReportAsync(int runId, PublishAnalysisReportRequest request, CancellationToken ct = default);
    Task<AnalysisRunGateDto> GetRunGateAsync(
        int runId, string? scope = null, CancellationToken ct = default);
    Task<AnalysisRunGateDto> GetRunGateAsync(int runId, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisFindingDto>> GetFindingsAsync(int projectId, AnalysisFindingPaginationRequest request, CancellationToken ct = default);
    Task<AnalysisFindingDto?> GetFindingAsync(int findingId, CancellationToken ct = default);
    Task<List<AnalysisFindingOccurrenceDto>> GetFindingOccurrencesAsync(int findingId, int take, CancellationToken ct = default);
    Task<int?> GetFindingProjectIdAsync(int findingId, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisReportDto>> GetReportsAsync(int projectId, PaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisPortfolioRowDto>> GetPortfolioAsync(
        IReadOnlyCollection<int>? accessibleProjectIds,
        AnalysisPortfolioPaginationRequest request,
        CancellationToken ct = default);
    Task<List<AnalysisPortfolioProjectDto>> GetPortfolioProjectsAsync(
        IReadOnlyCollection<int>? accessibleProjectIds,
        CancellationToken ct = default);
    Task<AnalysisPortfolioFilterValuesDto> GetPortfolioFilterValuesAsync(
        IReadOnlyCollection<int>? accessibleProjectIds,
        CancellationToken ct = default);
    Task<AnalysisProjectSummaryDto> GetProjectSummaryAsync(int projectId, CancellationToken ct = default);
    Task<List<AnalysisPolicyDto>> GetPoliciesAsync(int projectId, CancellationToken ct = default);
    Task<AnalysisPolicyDto> CreatePolicyAsync(int projectId, UpsertAnalysisPolicyRequest request, CancellationToken ct = default);
    Task<AnalysisPolicyDto> UpdatePolicyAsync(int projectId, int policyId, UpsertAnalysisPolicyRequest request, CancellationToken ct = default);
    Task<List<AnalysisPolicyDto>> GetScopedPoliciesAsync(int? organizationId, CancellationToken ct = default);
    Task<AnalysisPolicyDto> CreateScopedPolicyAsync(int? organizationId, UpsertAnalysisPolicyRequest request, CancellationToken ct = default);
    Task<AnalysisPolicyDto> UpdateScopedPolicyAsync(int? organizationId, int policyId, UpsertAnalysisPolicyRequest request, CancellationToken ct = default);
    Task<List<AnalysisPolicyDto>> ApplyPolicyBatchAsync(
        int? organizationId,
        int? projectId,
        ApplyAnalysisPolicyBatchRequest request,
        CancellationToken ct = default);
    Task<AnalysisPolicySetPreviewDto> PreviewPolicySetAsync(
        int? organizationId,
        int? projectId,
        PreviewAnalysisPolicySetRequest request,
        CancellationToken ct = default);
    Task<List<AnalysisPolicyRevisionDto>> GetPolicyRevisionsAsync(
        int? organizationId, int? projectId, int policyId, CancellationToken ct = default);
    Task<AnalysisPolicyDto> RollbackPolicyAsync(
        int? organizationId, int? projectId, int policyId, int version, CancellationToken ct = default);
    Task<List<AnalysisPolicyExceptionDto>> GetExceptionsAsync(int projectId, CancellationToken ct = default);
    Task<AnalysisPolicyExceptionDto> CreateExceptionAsync(int projectId, CreateAnalysisPolicyExceptionRequest request, string actor, CancellationToken ct = default);
    Task RevokeExceptionAsync(int projectId, int exceptionId, string actor, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisMetricDto>> GetMetricsAsync(int projectId, AnalysisMetricPaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisComponentDto>> GetComponentsAsync(int projectId, AnalysisComponentPaginationRequest request, CancellationToken ct = default);
    Task<AnalysisTrackingStatusDto?> GetTrackingStatusAsync(int projectId, CancellationToken ct = default);
    Task<PaginatedResult<AnalysisVulnerabilityObservationDto>> GetVulnerabilityObservationsAsync(
        int projectId, PaginationRequest request, CancellationToken ct = default);
}
