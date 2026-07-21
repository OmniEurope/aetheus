// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectQualitySection : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    private bool _loading = true;
    private ProjectQualityTrendDto _trend = new();
    private List<CoveragePoint> _coverage = [];
    private List<TestPoint> _tests = [];
    private List<ComplexityPoint> _complexity = [];

    private bool HasAny => _coverage.Count > 0 || _tests.Count > 0 || _complexity.Count > 0;

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        try
        {
            _trend = await Api.GetProjectQualityTrendAsync(ProjectId);
        }
        catch (HttpRequestException) { _trend = new(); }

        // Category axis uses the run id (culture-invariant, stable) rather than a locale-formatted date.
        _coverage = _trend.Coverage
            .Select(c => new CoveragePoint($"#{c.RunId}", Math.Round(c.LineRate * 100, 1), Math.Round(c.BranchRate * 100, 1)))
            .ToList();
        _tests = _trend.Tests
            .Select(t => new TestPoint($"#{t.RunId}", t.Passed, t.Failed))
            .ToList();
        _complexity = _trend.Complexity
            .Where(c => c.CrapAvg.HasValue)
            .Select(c => new ComplexityPoint($"#{c.RunId}", Math.Round(c.CrapAvg!.Value, 1), Math.Round(c.AvgCyclomatic, 1)))
            .ToList();

        _loading = false;
    }

    private sealed record CoveragePoint(string Label, double LinePct, double BranchPct);
    private sealed record TestPoint(string Label, int Passed, int Failed);
    private sealed record ComplexityPoint(string Label, double Crap, double AvgCc);
}
