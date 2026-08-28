// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal sealed record PipelineTemplateSelection(
    string Yaml,
    string BaseYaml,
    List<PipelineTemplateParameterDefinition> Parameters);
