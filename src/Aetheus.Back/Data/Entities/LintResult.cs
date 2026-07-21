// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Structured lint result for a pipeline run, parsed from a SARIF report (the tool-agnostic
/// static-analysis interchange format). Mirrors <see cref="CoverageResult"/>: one row per
/// <c>type: lint</c> step, the run view surfaces the latest.
/// </summary>
public class LintResult
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string? StageName { get; set; }
    public string? StepName { get; set; }

    /// <summary>The analyzer that produced the report (SARIF <c>tool.driver.name</c>), e.g. "ESLint".</summary>
    public string? Tool { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
}
