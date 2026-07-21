// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectQualitySectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectQualitySectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_CoverageTable_WhenTrendHasData()
    {
        // A single point stays below the >=2 chart threshold, so the numeric table renders without a
        // RadzenChart (Radzen charts need JS sizing that bUnit can't provide). This still proves the
        // section loads the trend and maps it to per-run rows.
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _handler.SetJsonResponse("api/pipelines/projects/1/quality-trend", new ProjectQualityTrendDto
        {
            Coverage = [new CoverageTrendPointDto { RunId = 10, Date = date, LineRate = 0.80, BranchRate = 0.60 }],
            Tests = [new TestTrendPointDto { RunId = 10, Date = date, Passed = 42, Failed = 0, Skipped = 1 }]
        });

        var cut = Render<ProjectQualitySection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("#10"), TimeSpan.FromSeconds(3));
        Assert.Contains("#10", cut.Markup);
        Assert.Contains("QualityTrend", cut.Markup);
    }

    // NOTE (audit F-MON-13): the >=2-point path renders a RadzenChart, whose OnAfterRenderAsync throws a
    // NullReferenceException under bUnit (it needs the real charting JS module bUnit cannot provide). So the
    // chart render is verified LIVE via the E2E suite (Playwright), per the finding's own "vérifier live"
    // action - a bUnit test of the chart is structurally impossible, not merely skipped.

    [Fact]
    public void Renders_EmptyState_WhenNoData()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());

        var cut = Render<ProjectQualitySection>(p => p.Add(c => c.ProjectId, 2));

        cut.WaitForState(() => cut.Markup.Contains("NoQualityData"), TimeSpan.FromSeconds(3));
        Assert.Contains("NoQualityData", cut.Markup);
    }
}
