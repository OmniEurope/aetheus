// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Generic per-run metric (key · type · value · unit · threshold), keyed to a <see cref="PipelineRun"/>.
/// One row per metric a pipeline step publishes - coverage (K), code complexity / CRAP (L), LOC and
/// other "last release" stats (F-bis), build duration, artifact size, test count… The shared shape
/// avoids a bespoke table per metric family: a metric step writes rows here and the UI reads them,
/// so new metric kinds need no schema or UI rewiring.
/// </summary>
public class RunMetric
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }

    /// <summary>Stage that produced the metric (null for run-level metrics).</summary>
    public string? StageName { get; set; }

    /// <summary>Step that produced the metric (null for run-level / stage-level metrics).</summary>
    public string? StepName { get; set; }

    /// <summary>Stable metric identifier, e.g. <c>coverage.line</c>, <c>loc.total</c>, <c>complexity.crap</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Semantic kind driving how the UI renders <see cref="Value"/>.</summary>
    public RunMetricType Type { get; set; }

    public double Value { get; set; }

    /// <summary>Display unit, e.g. <c>%</c>, <c>lines</c>, <c>s</c>, <c>bytes</c> - null when unitless.</summary>
    public string? Unit { get; set; }

    /// <summary>Optional threshold for semantic colouring (e.g. fail under 50% coverage). Null = no threshold.</summary>
    public double? Threshold { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
}
