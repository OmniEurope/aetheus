// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

internal sealed record PipelineFleetRow(
    int PipelineId,
    string PipelineName,
    string Description,
    string YamlDefinition,
    string? SourceBranch,
    int? ProjectId,
    int? OwnerProjectId,
    int? EnvironmentId,
    int? ProjectServerId,
    string OwnerName,
    string OwnerType,
    int OrganizationId);
