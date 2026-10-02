// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

/// <summary>
/// Read port implemented by Analysis and read by Projects for the project list. It lives here, in the
/// module that implements it (L7), so Projects (L8) depends downward on Analysis instead of Analysis
/// reaching up into Projects to implement it (layer guard, 2026-09-25).
/// </summary>
public interface IProjectAnalysisGradeReader
{
    Task<Dictionary<int, AnalysisGradeSummaryDto>> GetGradesAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default);
}
