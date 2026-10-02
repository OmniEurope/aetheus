// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Pipelines;

public sealed record PipelineTemplateSummaryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Version { get; init; }
    public int OrganizationId { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int PipelineCount { get; init; }
    public DateTime? LatestRunAt { get; init; }
}

public sealed record PipelineTemplateDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string YamlContent { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Changelog { get; init; } = string.Empty;
    public int OrganizationId { get; init; }
    public List<PipelineTemplateVersionDto> Versions { get; init; } = [];
}

public sealed record PipelineTemplateVersionDto
{
    public int Version { get; init; }
    public string YamlContent { get; init; } = string.Empty;
    public string ChangelogEntry { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
}

public sealed record PipelineTemplateVersionSummaryDto
{
    public int Version { get; init; }
    public string ChangelogEntry { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
}

public sealed record CreatePipelineTemplateRequest
{
    [Required, StringLength(100)] public string Name { get; init; } = string.Empty;
    [StringLength(500)] public string Description { get; init; } = string.Empty;
    [Required, StringLength(50)] public string Category { get; init; } = string.Empty;
    [Required, StringLength(50_000)] public string YamlContent { get; init; } = string.Empty;
    public int? OrganizationId { get; init; }
    [StringLength(500)] public string ChangelogEntry { get; init; } = "Initial version";
}

public sealed record UpdatePipelineTemplateRequest
{
    [Required, StringLength(100)] public string Name { get; init; } = string.Empty;
    [StringLength(500)] public string Description { get; init; } = string.Empty;
    [Required, StringLength(50)] public string Category { get; init; } = string.Empty;
    [Required, StringLength(50_000)] public string YamlContent { get; init; } = string.Empty;
    [Required, MinLength(1), StringLength(500)] public string ChangelogEntry { get; init; } = string.Empty;
}
