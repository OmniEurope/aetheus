// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.AiTasks;

public sealed record AiRunnerProfileDto
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Binary { get; init; } = string.Empty;
    public List<string> ArgsTemplate { get; init; } = [];
    public Dictionary<string, string> Environment { get; init; } = [];
    public int TimeoutSeconds { get; init; }
    public int MaxOutputBytes { get; init; }
    public bool SendsDataExternally { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public abstract record AiRunnerProfileRequest
{
    [Required]
    [StringLength(120)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [Required]
    [StringLength(260)]
    public string Binary { get; init; } = string.Empty;

    [MaxItemStringLength(1024)]
    public List<string> ArgsTemplate { get; init; } = [];

    [BoundedDictionary(maxEntries: 32, maxKeyLength: 128, maxValueLength: 4096)]
    public Dictionary<string, string> Environment { get; init; } = [];

    [Range(1, 86400)]
    public int TimeoutSeconds { get; init; } = 600;

    [Range(1024, 1_000_000)]
    public int MaxOutputBytes { get; init; } = 200_000;

    public bool SendsDataExternally { get; init; }
}

public sealed record CreateAiRunnerProfileRequest : AiRunnerProfileRequest
{
    public int? OrganizationId { get; init; }
}

public sealed record UpdateAiRunnerProfileRequest : AiRunnerProfileRequest;

public sealed record AiTaskDefinitionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public bool ProfileSendsDataExternally { get; init; }
    public string PromptTemplate { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? ServerId { get; init; }
    public string? ServerName { get; init; }
    public string? Schedule { get; init; }
    public bool Enabled { get; init; }
    public List<string> EventTypes { get; init; } = [];
    public DateTime? LastScheduledAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public abstract record AiTaskDefinitionRequest : IValidatableObject
{
    [Required]
    [StringLength(120)]
    public string Name { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ProfileId { get; init; }

    [Required]
    [StringLength(20_000)]
    public string PromptTemplate { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? ServerId { get; init; }

    [StringLength(120)]
    public string? Schedule { get; init; }

    public bool Enabled { get; init; } = true;

    [MaxItemStringLength(120)]
    public List<string> EventTypes { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProjectId.HasValue == ServerId.HasValue)
            yield return new ValidationResult(
                "Exactly one owner must be set: ProjectId or ServerId.",
                [nameof(ProjectId), nameof(ServerId)]);
    }
}

public sealed record CreateAiTaskDefinitionRequest : AiTaskDefinitionRequest;

public sealed record UpdateAiTaskDefinitionRequest : AiTaskDefinitionRequest;

public sealed record AiRunResultDto
{
    public int Id { get; init; }
    public int ServerTaskId { get; init; }
    public int? PipelineRunId { get; init; }
    public int? AiTaskDefinitionId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public bool SendsDataExternally { get; init; }
    public string ReportMarkdown { get; init; } = string.Empty;
    public AiVerdict Verdict { get; init; }
    public string? DiffPatch { get; init; }
    public long DurationMs { get; init; }
    public bool Truncated { get; init; }
    public bool Succeeded { get; init; }
    public int? ProjectId { get; init; }
    public int? SourceRepositoryId { get; init; }
    public string? BaseCommitSha { get; init; }
    public int? ProposedRepositoryId { get; init; }
    public string? ProposedBranchName { get; init; }
    public string? ProposedCommitSha { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record AiPatchApplicationDto
{
    public int RepositoryId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public string CommitSha { get; init; } = string.Empty;
}

public sealed record AiRunStartDto
{
    public int TaskId { get; init; }
}

public sealed record PublishAiRunResultRequest
{
    [Range(1, int.MaxValue)]
    public int ServerTaskId { get; init; }

    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string ProfileName { get; init; } = string.Empty;
    public bool SendsDataExternally { get; init; }

    [Required]
    [StringLength(1_000_000)]
    public string ReportMarkdown { get; init; } = string.Empty;

    public AiVerdict Verdict { get; init; }

    [StringLength(1_000_000)]
    public string? DiffPatch { get; init; }

    [Range(0, long.MaxValue)]
    public long DurationMs { get; init; }

    [Range(1, int.MaxValue)]
    public int? SourceRepositoryId { get; init; }

    [StringLength(64, MinimumLength = 40)]
    public string? BaseCommitSha { get; init; }

    public bool Truncated { get; init; }
    public bool Succeeded { get; init; }
}

public sealed record AiConsumptionDto
{
    public int RunCount { get; init; }
    public int FailedCount { get; init; }
    public long DurationMs { get; init; }
    public List<AiProfileConsumptionDto> Profiles { get; init; } = [];
}

public sealed record AiProfileConsumptionDto
{
    public string ProfileName { get; init; } = string.Empty;
    public int RunCount { get; init; }
    public int FailedCount { get; init; }
    public long DurationMs { get; init; }
}
