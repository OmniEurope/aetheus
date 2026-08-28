// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

internal sealed record FindingQuery(
    int Page,
    int PageSize,
    string? Search,
    AnalysisCategory? Category,
    AnalysisSeverity? Severity,
    AnalysisFindingStatus? Status,
    bool? IsNew,
    string? Scanner,
    string? Branch,
    string? Responsible,
    string? SortBy,
    bool SortDescending);
