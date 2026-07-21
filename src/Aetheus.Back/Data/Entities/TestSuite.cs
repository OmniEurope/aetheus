// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class TestSuite
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TestSuiteType Type { get; set; } = TestSuiteType.Automated;
    public int? PipelineId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public Pipeline? Pipeline { get; set; }
    public List<TestCase> TestCases { get; set; } = [];
}

public class TestCase
{
    public int Id { get; set; }
    public int TestSuiteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AutomatedTestClass { get; set; }
    public string? AutomatedTestMethod { get; set; }
    public TestOutcome? LastOutcome { get; set; }
    public int? LastPipelineRunId { get; set; }
    public double? LastDurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public TestSuite TestSuite { get; set; } = null!;
    public PipelineRun? LastPipelineRun { get; set; }
}
