// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.ProjectDetailSections;

public partial class ProjectPipelinesSection
{
    [Parameter, EditorRequired] public List<PipelineDto>? Pipelines { get; set; }
    [Parameter, EditorRequired] public int ProjectId { get; set; }
}
