// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.ProjectDetailSections;

internal sealed record FindingQuery(
    int Page,
    int PageSize,
    string? Search,
    string Categories,
    string Severities,
    string Statuses,
    bool? IsNew,
    string? Scanner,
    string? Branch,
    string? Responsible,
    string? SortBy,
    bool SortDescending,
    int? IdFrom = null,
    int? IdTo = null,
    int? IdNot = null);
