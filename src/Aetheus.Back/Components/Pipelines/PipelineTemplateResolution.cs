// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public sealed record PipelineTemplateResolution(
    PipelineYamlDefinition Definition,
    string Yaml,
    string? TemplateName,
    int? RequestedVersion,
    int? ResolvedVersion,
    bool UsesLegacyReference);
