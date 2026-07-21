// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Shared;

public partial class PipelineRunHistory
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<PipelineRunSummaryDto> Runs { get; set; } = [];
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public int? ServerId { get; set; }

    private IReadOnlyList<PipelineRunSummaryDto> DisplayRuns => Runs.Take(5).Reverse().ToList();

    private string RunHref(int runId) => PipelineRunPresentation.Href(runId, ProjectId, ServerId);

    private string RunTitle(PipelineRunSummaryDto run) =>
        $"#{run.Id} - {L.Localize(run.Status)} - {run.StartedAt:g}";
}
