// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Analysis;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Analysis;

public sealed class AnalysisFindingDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AnalysisFindingDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void RendersBoundedSourceAndPipelineRunLink()
    {
        Services.GetRequiredService<PermissionService>().SetPermissions([], isAdmin: true);
        var occurrence = new AnalysisFindingOccurrenceDto
        {
            Id = 9,
            AnalysisReportId = 12,
            PipelineRunId = 88,
            RepositoryId = 42,
            ToolName = "OpenGrep",
            ScannerKey = "opengrep",
            RuleId = "aetheus.test.rule",
            FilePath = "src/Sample.cs",
            StartLine = 42,
            EndLine = 46,
            Symbol = "Sample.Execute",
            Message = "Untrusted input reaches a command sink.",
            BranchName = "feature/security",
            CommitHash = "0123456789abcdef",
            IsNew = true,
            CreatedAt = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc)
        };
        _handler.SetJsonResponse("api/analysis/findings/7", new AnalysisFindingDto
        {
            Id = 7,
            ProjectId = 1,
            RuleId = occurrence.RuleId,
            Title = "Command injection",
            Message = "User-controlled data can execute an operating-system command.",
            Category = AnalysisCategory.Sast,
            Severity = AnalysisSeverity.High,
            Status = AnalysisFindingStatus.Open,
            LatestOccurrence = occurrence
        });
        _handler.SetJsonResponse("api/analysis/findings/7/occurrences?take=100", new List<AnalysisFindingOccurrenceDto> { occurrence });
        _handler.SetJsonResponse("api/analysis/findings/7/decisions", new List<AnalysisFindingDecisionDto>());

        var cut = Render<AnalysisFindingDetail>(parameters => parameters.Add(component => component.Id, 7));

        cut.WaitForState(() => cut.Markup.Contains("src/Sample.cs", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        Assert.Contains("Sample.Execute", cut.Markup);
        Assert.Contains("Untrusted input reaches a command sink.", cut.Markup);
        Assert.Contains("pipelines/runs/88", cut.Markup);
        Assert.Equal("src/Sample.cs", cut.Find(".analysis-source-location code").TextContent);
        Assert.Contains("Line 42-46", cut.Find(".analysis-source-location").TextContent);
        var fileLink = cut.Find(".analysis-source-location-link");
        Assert.Contains("/git-repositories/42?tab=files", fileLink.GetAttribute("href"));
        Assert.Contains("ref=0123456789abcdef", fileLink.GetAttribute("href"));
        Assert.Contains("path=src%2FSample.cs", fileLink.GetAttribute("href"));
        Assert.Contains("line=42", fileLink.GetAttribute("href"));
        Assert.Contains("OpenGrep", cut.Find(".analysis-source-metadata").TextContent);
        Assert.Contains("feature/security", cut.Find(".analysis-source-metadata").TextContent);
        Assert.Contains("0123456789abcdef", cut.Find(".analysis-source-metadata").TextContent);
        Assert.Contains("User-controlled data can execute an operating-system command.", cut.Find(".analysis-finding-problem").TextContent);
        Assert.Contains("Untrusted input reaches a command sink.", cut.Find(".analysis-source-excerpt").TextContent);
        Assert.Equal(4, cut.FindAll(".analysis-finding-tabs .rz-tabview-nav button").Count);
        Assert.DoesNotContain("analysis-finding-prompt-source", cut.Markup);
        Assert.DoesNotContain("analysis-decision-card", cut.Markup);
    }

    [Fact]
    public async Task PromptActions_CopyDownloadAndPreviewMarkdown()
    {
        _handler.SetJsonResponse("api/analysis/findings/8", new AnalysisFindingDto
        {
            Id = 8,
            ProjectId = 1,
            RuleId = "security.secret",
            Title = "Secret exposed",
            Message = "A credential is committed.",
            Category = AnalysisCategory.Secrets,
            Severity = AnalysisSeverity.Critical,
            Status = AnalysisFindingStatus.Open
        });
        _handler.SetJsonResponse("api/analysis/findings/8/occurrences?take=100", new List<AnalysisFindingOccurrenceDto>());
        _handler.SetJsonResponse("api/analysis/findings/8/decisions", new List<AnalysisFindingDecisionDto>());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/analysis/findings/8?tab=prompt");
        var cut = Render<AnalysisFindingDetail>(parameters => parameters.Add(component => component.Id, 8));
        cut.WaitForState(() => cut.Markup.Contains("AnalysisAiPrompt", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        var buttons = cut.FindAll("button");
        await buttons.Single(button => button.TextContent.Contains("CopyAiPrompt", StringComparison.Ordinal)).ClickAsync(new());
        await buttons.Single(button => button.TextContent.Contains("DownloadMarkdown", StringComparison.Ordinal)).ClickAsync(new());
        await buttons.Single(button => button.TextContent.Contains("AnalysisPromptPreview", StringComparison.Ordinal)).ClickAsync(new());

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "navigator.clipboard.writeText");
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "downloadFile");
        Assert.Contains("analysis-finding-prompt-preview", cut.Markup);
    }

    [Fact]
    public void DecisionTab_RendersDecisionFormAndHistory()
    {
        Services.GetRequiredService<PermissionService>().SetPermissions([], isAdmin: true);
        _handler.SetJsonResponse("api/analysis/findings/9", new AnalysisFindingDto
        {
            Id = 9,
            ProjectId = 1,
            RuleId = "security.decision",
            Title = "Review required",
            Message = "This finding needs an explicit decision.",
            Category = AnalysisCategory.Sast,
            Severity = AnalysisSeverity.Medium,
            Status = AnalysisFindingStatus.Open
        });
        _handler.SetJsonResponse("api/analysis/findings/9/occurrences?take=100", new List<AnalysisFindingOccurrenceDto>());
        _handler.SetJsonResponse("api/analysis/findings/9/decisions", new List<AnalysisFindingDecisionDto>());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/analysis/findings/9?tab=decisions");

        var cut = Render<AnalysisFindingDetail>(parameters => parameters.Add(component => component.Id, 9));

        cut.WaitForState(() => cut.Markup.Contains("analysis-decision-card", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        Assert.Contains("AnalysisDecisionHelp", cut.Find(".analysis-decision-card").TextContent);
        Assert.Equal(3, cut.FindAll(".analysis-decision-choice").Count);
        Assert.Equal(2, cut.FindAll(".analysis-decision-details .rz-form-field").Count);
        Assert.Empty(cut.FindAll(".analysis-decision-outcome .rz-dropdown"));
        Assert.Contains("AnalysisDecisionReasonHint", cut.Find(".analysis-decision-card").TextContent);
        Assert.Contains("AnalysisDecisionHistory", cut.Markup);
    }

    [Fact]
    public async Task DecisionTab_SubmitsAnExplicitAuditableDecision()
    {
        Services.GetRequiredService<PermissionService>().SetPermissions([], isAdmin: true);
        _handler.SetJsonResponse("api/analysis/findings/10", new AnalysisFindingDto
        {
            Id = 10,
            ProjectId = 1,
            RuleId = "security.review",
            Title = "Review required",
            Message = "This finding needs an explicit decision.",
            Category = AnalysisCategory.Sast,
            Severity = AnalysisSeverity.Medium,
            Status = AnalysisFindingStatus.Open
        });
        _handler.SetJsonResponse("api/analysis/findings/10/occurrences?take=100", new List<AnalysisFindingOccurrenceDto>());
        _handler.SetJsonResponse("api/analysis/findings/10/decisions", new List<AnalysisFindingDecisionDto>());
        _handler.SetJsonResponse(HttpMethod.Post, "api/analysis/findings/10/decisions", new AnalysisFindingDecisionDto
        {
            Id = 3,
            AnalysisFindingId = 10,
            Status = AnalysisFindingStatus.Accepted,
            Reason = "Risk accepted after architecture review.",
            CreatedByUsername = "admin"
        });
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/analysis/findings/10?tab=decisions");
        var cut = Render<AnalysisFindingDetail>(parameters => parameters.Add(component => component.Id, 10));
        cut.WaitForState(() => cut.Markup.Contains("analysis-decision-card", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        cut.Find("textarea").Change("Risk accepted after architecture review.");
        cut.Find("form.analysis-decision-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, request =>
            request.Method == "POST" && request.Url.Contains("api/analysis/findings/10/decisions", StringComparison.Ordinal)));
    }
}
