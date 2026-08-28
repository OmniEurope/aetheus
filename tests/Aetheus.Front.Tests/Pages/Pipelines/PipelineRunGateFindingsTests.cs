// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineRunGateFindingsTests : BunitContext
{
    public PipelineRunGateFindingsTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public async Task Actions_OpenProjectQuality_AndExportEveryGateFinding()
    {
        var gate = new AnalysisRunGateDto
        {
            PipelineRunId = 1586,
            FindingCount = 2,
            Findings =
            [
                new AnalysisRunGateFindingDto
                {
                    FindingId = 10,
                    RuleId = "rule-one",
                    Title = "First finding",
                    Message = "First evidence",
                    Category = AnalysisCategory.Sast,
                    Severity = AnalysisSeverity.High,
                    Status = AnalysisFindingStatus.Open,
                    FilePath = "src/First.cs",
                    StartLine = 12
                },
                new AnalysisRunGateFindingDto
                {
                    FindingId = 11,
                    RuleId = "rule-two",
                    Title = "Second finding",
                    Message = "Second evidence",
                    Category = AnalysisCategory.CodeQuality,
                    Severity = AnalysisSeverity.Low,
                    Status = AnalysisFindingStatus.Open,
                    FilePath = "src/Second.cs",
                    StartLine = 24
                }
            ]
        };
        var cut = Render<PipelineRunGateFindings>(parameters => parameters
            .Add(component => component.Gate, gate)
            .Add(component => component.ProjectId, 7));

        Assert.Equal(4, cut.FindAll(".analysis-run-gate-metrics > div").Count);
        Assert.Contains("aetheus-grid-fullheight", cut.Find(".analysis-run-findings-grid").ClassList);

        await cut.FindAll("button").Single(button =>
            button.TextContent.Contains("AnalysisExportAllAiPrompt", StringComparison.Ordinal)).ClickAsync(new());

        var download = Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "downloadFile");
        var markdown = Assert.IsType<string>(download.Arguments[1]);
        Assert.Contains("First finding", markdown);
        Assert.Contains("Second finding", markdown);
        Assert.Contains("First evidence", markdown);
        Assert.Contains("Second evidence", markdown);

        cut.FindAll("button").Single(button =>
            button.TextContent.Contains("AnalysisOpenProjectQuality", StringComparison.Ordinal)).Click();

        Assert.EndsWith(
            "/projects/7/quality",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
    }
}
