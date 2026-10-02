// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

internal sealed class PipelineModel
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string YamlDefinition { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public string? SourceBranch { get; set; }
    public int? EnvironmentId { get; set; }
    public int? ProjectServerId { get; set; }

    public CreatePipelineRequest ToCreateRequest() => new()
    {
        Name = Name,
        Description = Description,
        YamlDefinition = YamlDefinition,
        ProjectId = ProjectId,
        SourceBranch = SourceBranch,
        EnvironmentId = EnvironmentId,
        ProjectServerId = ProjectServerId
    };

    public UpdatePipelineRequest ToUpdateRequest() => new()
    {
        Name = Name,
        Description = Description,
        YamlDefinition = YamlDefinition,
        ProjectId = ProjectId,
        SourceBranch = SourceBranch,
        EnvironmentId = EnvironmentId,
        ProjectServerId = ProjectServerId
    };
}
