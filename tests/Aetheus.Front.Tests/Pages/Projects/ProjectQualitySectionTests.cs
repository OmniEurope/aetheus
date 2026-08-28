// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectQualitySectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectQualitySectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void FindingQuery_ValuesContainingTheFormerSeparatorRemainDistinct()
    {
        var first = new FindingQuery(1, 25, "owner|main", null, null, null, null, null, null, "team", null, false);
        var second = new FindingQuery(1, 25, "owner", null, null, null, null, null, "main", "team", null, false);

        Assert.NotEqual(first, second);
    }

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
        MockAnalysisEndpoints(1);

        var cut = Render<ProjectQualitySection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("#10"), TimeSpan.FromSeconds(3));
        Assert.Contains("#10", cut.Markup);
        Assert.Contains("CoverageTrend", cut.Markup);
    }

    // NOTE (audit F-MON-13): the >=2-point path renders a RadzenChart, whose OnAfterRenderAsync throws a
    // NullReferenceException under bUnit (it needs the real charting JS module bUnit cannot provide). So the
    // chart render is verified LIVE via the E2E suite (Playwright), per the finding's own "vérifier live"
    // action - a bUnit test of the chart is structurally impossible, not merely skipped.

    [Fact]
    public void Renders_EmptyState_WhenNoData()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);

        var cut = Render<ProjectQualitySection>(p => p.Add(c => c.ProjectId, 2));

        cut.WaitForState(() => cut.Markup.Contains("NoQualityData"), TimeSpan.FromSeconds(3));
        Assert.Contains("NoQualityData", cut.Markup);
    }

    [Fact]
    public void Renders_GlobalGradeBeforeDomainDetails()
    {
        _handler.SetJsonResponse("api/pipelines/projects/4/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(4);
        _handler.SetJsonResponse("api/analysis/projects/4/summary", new AnalysisProjectSummaryDto
        {
            Grade = new AnalysisGradeSummaryDto
            {
                OverallGrade = AnalysisGrade.B,
                Completeness = AnalysisGradeCompleteness.Complete,
                LimitingDomain = AnalysisGradeDomain.CodeQuality,
                CommitHash = new string('a', 40),
                EvaluatedAt = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc),
                Domains =
                [
                    new AnalysisGradeDomainDto
                    {
                        Domain = AnalysisGradeDomain.CodeQuality,
                        Grade = AnalysisGrade.B,
                        Required = true,
                        Completeness = AnalysisGradeCompleteness.Complete,
                        EvaluatedAt = new DateTime(2025, 12, 31, 12, 0, 0, DateTimeKind.Utc),
                        Measures =
                        [
                            new AnalysisGradeMeasureDto
                            {
                                Key = "grade.duplication",
                                Domain = AnalysisGradeDomain.CodeQuality,
                                Grade = AnalysisGrade.B,
                                Required = true,
                                Observed = true,
                                ObservedValue = 4,
                                Unit = "percent"
                            }
                        ]
                    }
                ]
            }
        });

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(
            component => component.ProjectId,
            4));

        cut.WaitForState(() => cut.Markup.Contains("AnalysisGlobalGrade"), TimeSpan.FromSeconds(3));
        Assert.Contains("analysis-grade-letter", cut.Markup);
        Assert.Contains("AnalysisDuplicatedCode", cut.Markup);
        Assert.DoesNotContain(">grade.duplication<", cut.Markup);
        Assert.Contains("aaaaaaaaaaaa", cut.Markup);
        Assert.Contains("AnalysisFreshness", cut.Markup);
        Assert.Contains("2025", cut.Markup);
    }

    [Fact]
    public void Groups_CodeQuality_And_Tests_With_WorstGrade_And_CrapScore()
    {
        _handler.SetJsonResponse("api/pipelines/projects/5/quality-trend", new ProjectQualityTrendDto
        {
            Complexity =
            [
                new ComplexityTrendPointDto
                {
                    RunId = 51,
                    Date = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc),
                    AvgCyclomatic = 3.2,
                    MaxCyclomatic = 25,
                    CrapAvg = 8.2
                }
            ]
        });
        MockAnalysisEndpoints(5);
        _handler.SetJsonResponse("api/analysis/projects/5/summary", new AnalysisProjectSummaryDto
        {
            Grade = new AnalysisGradeSummaryDto
            {
                OverallGrade = AnalysisGrade.C,
                Completeness = AnalysisGradeCompleteness.Complete,
                LimitingDomain = AnalysisGradeDomain.CodeQuality,
                Domains =
                [
                    new AnalysisGradeDomainDto
                    {
                        Domain = AnalysisGradeDomain.Reliability,
                        Grade = AnalysisGrade.A,
                        Required = true,
                        Completeness = AnalysisGradeCompleteness.Complete,
                        Measures =
                        [
                            new AnalysisGradeMeasureDto
                            {
                                Key = "grade.coverage.line",
                                Domain = AnalysisGradeDomain.Reliability,
                                Grade = AnalysisGrade.A,
                                Observed = true,
                                ObservedValue = 75.9,
                                Unit = "percent"
                            }
                        ]
                    },
                    new AnalysisGradeDomainDto
                    {
                        Domain = AnalysisGradeDomain.CodeQuality,
                        Grade = AnalysisGrade.C,
                        Required = true,
                        Completeness = AnalysisGradeCompleteness.Complete,
                        Measures =
                        [
                            new AnalysisGradeMeasureDto
                            {
                                Key = "grade.complexity.maximum",
                                Domain = AnalysisGradeDomain.CodeQuality,
                                Grade = AnalysisGrade.C,
                                Observed = true,
                                ObservedValue = 25
                            }
                        ]
                    }
                ]
            }
        });

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 5));

        cut.WaitForState(() => cut.Markup.Contains("AnalysisCrapAverage", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        var combined = Assert.Single(cut.FindAll(".analysis-grade-domain"), element =>
            element.TextContent.Contains("AnalysisCodeQualityAndTests", StringComparison.Ordinal));
        Assert.Contains("analysis-grade-c", combined.InnerHtml);
        Assert.Contains("AnalysisTestedLines", combined.TextContent);
        Assert.Contains("75", combined.TextContent);
        Assert.Contains("%", combined.TextContent);
        Assert.Contains("AnalysisMaximumComplexity", combined.TextContent);
        Assert.Contains("AnalysisCrapAverage", combined.TextContent);
        Assert.Matches("8[.,]2", combined.TextContent);
        Assert.DoesNotContain(">grade.", combined.InnerHtml);
    }

    [Fact]
    public void Uses_Client_RenderMode_With_Exactly_Two_Top_Tabs()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);
        _handler.SetJsonResponse(
            "api/analysis/projects/2/findings?page=1&pageSize=25&severity=Critical&status=Open",
            new PaginatedResult<AnalysisFindingDto>());

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 2));

        cut.WaitForState(() => cut.Markup.Contains("NoQualityData"), TimeSpan.FromSeconds(3));
        var tabs = cut.FindComponent<RadzenTabs>().Instance;
        Assert.Equal(TabRenderMode.Client, tabs.RenderMode);
        Assert.Equal(TabPosition.Top, tabs.TabPosition);
        Assert.Equal(2, cut.FindComponents<RadzenTabsItem>().Count);
    }

    [Fact]
    public void Overview_Does_Not_Load_The_Findings_Grid()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 2));

        cut.WaitForState(() => cut.Markup.Contains("NoQualityData", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(
            _handler.Requests,
            request => request.Url.Contains("api/analysis/projects/2/findings", StringComparison.Ordinal));
    }

    [Fact]
    public void SummaryShortcut_Opens_Filtered_Findings_And_Updates_The_Url()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);
        _handler.SetJsonResponse(
                            "api/analysis/projects/2/findings?page=1&pageSize=25&severity=Critical&status=Open",
            new PaginatedResult<AnalysisFindingDto>());

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 2));
        cut.WaitForElement("[data-quality-shortcut='critical']", TimeSpan.FromSeconds(3));

        cut.Find("[data-quality-shortcut='critical']").Click();

        cut.WaitForState(
            () => Services.GetRequiredService<NavigationManager>().Uri.Contains("tab=findings", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        cut.WaitForAssertion(() => Assert.Contains(
            _handler.Requests,
            request => request.Method == "GET"
                       && request.Url.Contains(
                           "api/analysis/projects/2/findings?page=1&pageSize=25&severity=Critical&status=Open",
                           StringComparison.Ordinal)));
    }

    [Fact]
    public void Opening_Findings_Loads_The_Lazy_Rendered_Grid()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);
        _handler.SetJsonResponse(
            "api/analysis/projects/2/findings?page=1&pageSize=25&status=Open",
            new PaginatedResult<AnalysisFindingDto>
            {
                Items =
                [
                    new AnalysisFindingDto
                    {
                        Id = 42,
                        ProjectId = 2,
                        RuleId = "live-grid",
                        Title = "Finding loaded on activation"
                    }
                ],
                TotalCount = 1,
                Page = 1,
                PageSize = 25
            });

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", "findings"));
        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 2));

        cut.WaitForState(
            () => cut.Markup.Contains("Finding loaded on activation", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        Assert.Contains("Finding loaded on activation", cut.Markup);
        Assert.Single(_handler.Requests, request => request.Url.Contains(
            "api/analysis/projects/2/findings?page=1&pageSize=25&status=Open",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Same_Project_Rerender_Does_Not_Reload_Shared_Quality_State()
    {
        _handler.SetJsonResponse("api/pipelines/projects/2/quality-trend", new ProjectQualityTrendDto());
        MockAnalysisEndpoints(2);

        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, 2));
        cut.WaitForState(
            () => _handler.Requests.Any(request => request.Url.Contains("/summary", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
        var summaryRequests = _handler.Requests.Count(request =>
            request.Url.Contains("api/analysis/projects/2/summary", StringComparison.Ordinal));

        cut.Render(parameters => parameters.Add(component => component.ProjectId, 2));

        Assert.Equal(summaryRequests, _handler.Requests.Count(request =>
            request.Url.Contains("api/analysis/projects/2/summary", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExportAllAiPrompt_LoadsEveryFilteredPage_AndDownloadsOneMarkdownFile()
    {
        const int projectId = 6;
        _handler.SetJsonResponse($"api/pipelines/projects/{projectId}/quality-trend", new ProjectQualityTrendDto());
        _handler.SetJsonResponse($"api/analysis/projects/{projectId}/summary", new AnalysisProjectSummaryDto());
        _handler.SetJsonResponse(
            $"api/analysis/projects/{projectId}/findings?page=1&pageSize=25&status=Open",
            new PaginatedResult<AnalysisFindingDto>
            {
                Items = [Finding(1, "Grid finding")],
                TotalCount = 201,
                Page = 1,
                PageSize = 25
            });
        _handler.SetJsonResponse(
            $"api/analysis/projects/{projectId}/findings?page=1&pageSize=200&status=Open",
            new PaginatedResult<AnalysisFindingDto>
            {
                Items = Enumerable.Range(1, 200).Select(id => Finding(id, $"Finding {id}")).ToList(),
                TotalCount = 201,
                Page = 1,
                PageSize = 200
            });
        _handler.SetJsonResponse(
            $"api/analysis/projects/{projectId}/findings?page=2&pageSize=200&status=Open",
            new PaginatedResult<AnalysisFindingDto>
            {
                Items = [Finding(201, "Final finding")],
                TotalCount = 201,
                Page = 2,
                PageSize = 200
            });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", "findings"));
        var cut = Render<ProjectQualitySection>(parameters => parameters.Add(component => component.ProjectId, projectId));
        cut.WaitForState(
            () => cut.FindAll("button").Any(button =>
                button.TextContent.Contains("AnalysisExportAllAiPrompt", StringComparison.Ordinal)
                && !button.HasAttribute("disabled")),
            TimeSpan.FromSeconds(3));

        Assert.Contains("aetheus-grid-fullheight", cut.Find(".analysis-table").ClassList);
        await cut.FindAll("button").Single(button =>
            button.TextContent.Contains("AnalysisExportAllAiPrompt", StringComparison.Ordinal)).ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "downloadFile"));
        Assert.Contains(_handler.Requests, request => request.Url.Contains(
            $"api/analysis/projects/{projectId}/findings?page=2&pageSize=200&status=Open",
            StringComparison.Ordinal));
        var download = Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "downloadFile");
        var markdown = Assert.IsType<string>(download.Arguments[1]);
        Assert.Contains("Finding 1", markdown);
        Assert.Contains("Final finding", markdown);
        Assert.Contains("AnalysisBulkAiPromptCount: 201", markdown);
    }

    private static AnalysisFindingDto Finding(int id, string title) => new()
    {
        Id = id,
        ProjectId = 6,
        RuleId = $"rule-{id}",
        Title = title,
        Message = $"Evidence {id}",
        Category = AnalysisCategory.CodeQuality,
        Severity = AnalysisSeverity.Low,
        Status = AnalysisFindingStatus.Open
    };

    private void MockAnalysisEndpoints(int projectId)
    {
        var prefix = $"api/analysis/projects/{projectId}";
        _handler.SetJsonResponse($"{prefix}/summary", new AnalysisProjectSummaryDto());
        _handler.SetJsonResponse($"{prefix}/findings?page=1&pageSize=25&status=Open", new PaginatedResult<AnalysisFindingDto>());
    }
}
