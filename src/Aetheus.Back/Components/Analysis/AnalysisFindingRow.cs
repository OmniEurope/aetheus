// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisFindingRow(
    AnalysisFinding Finding,
    AnalysisFindingOccurrence? LatestOccurrence,
    string? Responsible = null);
