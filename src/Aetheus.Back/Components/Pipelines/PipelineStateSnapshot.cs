// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Pipelines;

internal sealed record PipelineStateSnapshot(
    string Name,
    string Description,
    string YamlDefinition,
    PipelineTriggerType TriggerType,
    int? ProjectId,
    string? SourceBranch,
    int? EnvironmentId,
    int? ProjectServerId,
    DateTime UpdatedAt,
    string? CreatedByUsername)
{
    public static PipelineStateSnapshot Capture(Pipeline pipeline) => new(
        pipeline.Name, pipeline.Description, pipeline.YamlDefinition, pipeline.TriggerType,
        pipeline.ProjectId, pipeline.SourceBranch, pipeline.EnvironmentId,
        pipeline.ProjectServerId, pipeline.UpdatedAt, pipeline.CreatedByUsername);

    public void Restore(Pipeline pipeline)
    {
        pipeline.Name = Name;
        pipeline.Description = Description;
        pipeline.YamlDefinition = YamlDefinition;
        pipeline.TriggerType = TriggerType;
        pipeline.ProjectId = ProjectId;
        pipeline.SourceBranch = SourceBranch;
        pipeline.EnvironmentId = EnvironmentId;
        pipeline.ProjectServerId = ProjectServerId;
        pipeline.UpdatedAt = UpdatedAt;
        pipeline.CreatedByUsername = CreatedByUsername;
    }
}
