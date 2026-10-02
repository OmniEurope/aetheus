// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

internal sealed record PipelineTemplateSelection(
    string Yaml,
    string BaseYaml,
    List<PipelineTemplateParameterDefinition> Parameters);
