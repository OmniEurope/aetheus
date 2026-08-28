// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisPortfolioRow(
    int AnalysisReportId,
    int OrganizationId,
    string OrganizationName,
    int ProjectId,
    string ProjectName,
    int? PipelineId,
    string? PipelineName,
    int? PipelineRunId,
    string? BranchName,
    string? CommitHash,
    AnalysisCategory Category,
    string ScannerName,
    string ScannerVersion,
    AnalysisReportStatus Status,
    AnalysisGateStatus? GateStatus,
    AnalysisGrade? Grade,
    AnalysisGradeCompleteness GradeCompleteness,
    int FindingCount,
    int NewFindingCount,
    int BlockerCount,
    int WarningCount,
    DateTime CompletedAt);
