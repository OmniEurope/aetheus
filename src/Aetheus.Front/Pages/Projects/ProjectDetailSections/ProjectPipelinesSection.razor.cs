// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectPipelinesSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter, EditorRequired] public List<PipelineDto>? Pipelines { get; set; }
    [Parameter, EditorRequired] public int ProjectId { get; set; }

    private void NewPipeline() => Nav.NavigateTo($"/pipelines/new?projectId={ProjectId}");
}
