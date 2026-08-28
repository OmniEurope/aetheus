// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class TestResult
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string? StageName { get; set; }
    public string? StepName { get; set; }
    public string TestName { get; set; } = string.Empty;
    public string? TestSuite { get; set; }
    public TestOutcome Outcome { get; set; }
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
}
