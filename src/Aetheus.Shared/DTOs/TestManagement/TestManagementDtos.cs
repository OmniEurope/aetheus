// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

// --- T-01: Test Case Management ---

public abstract record TestSuiteBaseDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TestSuiteType Type { get; init; }
    public int? PipelineId { get; init; }
    public string? PipelineName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record TestSuiteDto : TestSuiteBaseDto
{
    public int TestCaseCount { get; init; }
    public int PassedCount { get; init; }
    public int FailedCount { get; init; }
}

public sealed record TestSuiteDetailDto : TestSuiteBaseDto
{
    public List<TestCaseDto> TestCases { get; init; } = [];
}

public sealed record CreateTestSuiteRequest
{
    public int ProjectId { get; init; }

    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public TestSuiteType Type { get; init; } = TestSuiteType.Automated;
    public int? PipelineId { get; init; }
}

public sealed record UpdateTestSuiteRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public TestSuiteType Type { get; init; }
    public int? PipelineId { get; init; }
}

public sealed record TestCaseDto
{
    public int Id { get; init; }
    public int TestSuiteId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? AutomatedTestClass { get; init; }
    public string? AutomatedTestMethod { get; init; }
    public TestOutcome? LastOutcome { get; init; }
    public int? LastPipelineRunId { get; init; }
    public double? LastDurationMs { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record IngestTestResultsRequest
{
    public int ProjectId { get; init; }
    public int? PipelineRunId { get; init; }

    [Required]
    [StringLength(200)]
    public string SuiteName { get; init; } = string.Empty;

    [Required]
    [StringLength(8 * 1024 * 1024)]
    public string XmlContent { get; init; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Format { get; init; } = "junit";
}

public sealed record TestIngestionResultDto
{
    public int SuiteId { get; init; }
    public int TotalCases { get; init; }
    public int NewCases { get; init; }
    public int UpdatedCases { get; init; }
}
