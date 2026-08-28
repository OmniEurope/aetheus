// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisReportRow(
    AnalysisReport Report,
    int FindingCount,
    int NewFindingCount,
    int ComponentCount,
    int MetricCount,
    AnalysisGateStatus? GateStatus,
    AnalysisGrade? Grade,
    AnalysisGradeCompleteness GradeCompleteness,
    int BlockerCount,
    int WarningCount);
