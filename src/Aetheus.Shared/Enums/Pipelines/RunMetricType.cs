// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// Semantic kind of a <c>RunMetric</c>, telling the UI how to render its value (gauge, raw count,
/// duration, byte size, score). The metric's <c>Key</c> identifies WHICH metric it is
/// (e.g. <c>coverage.line</c>, <c>loc.total</c>, <c>complexity.crap</c>); the type tells HOW to show it.
/// </summary>
public enum RunMetricType
{
    /// <summary>0–100 (or 0–1) ratio shown as a percentage / gauge - e.g. line coverage.</summary>
    Percentage = 0,

    /// <summary>Integer-ish count - e.g. lines of code, number of tests, finding count.</summary>
    Count = 1,

    /// <summary>Unbounded numeric score - e.g. cyclomatic complexity, CRAP score.</summary>
    Score = 2,

    /// <summary>Elapsed time in seconds - e.g. build duration.</summary>
    Duration = 3,

    /// <summary>Byte size - e.g. artifact size.</summary>
    Size = 4
}
