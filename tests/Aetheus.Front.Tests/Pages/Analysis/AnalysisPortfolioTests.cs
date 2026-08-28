// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Analysis;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Analysis;

public sealed class AnalysisPortfolioTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AnalysisPortfolioTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Filters_ExposeAccessibleNames()
    {
        _handler.SetPaginatedJsonResponse("api/projects", Array.Empty<ProjectDto>());
        _handler.SetJsonResponse("api/analysis/portfolio/projects", new List<AnalysisPortfolioProjectDto>());
        _handler.SetPaginatedJsonResponse("api/analysis/portfolio", Array.Empty<AnalysisPortfolioRowDto>());

        var cut = Render<AnalysisPortfolio>();

        cut.WaitForState(
            () => cut.FindAll("[aria-label='Search']").Count == 1,
            TimeSpan.FromSeconds(3));

        var expectedLabels = new[]
        {
            "Search",
            "Organization",
            "Project",
            "Pipeline",
            "Category",
            "Branch",
            "Commit",
            "From",
            "To"
        };

        var labelsWithInvalidCount = expectedLabels
            .Where(label => cut.FindAll($"[aria-label='{label}']").Count != 1)
            .ToArray();
        Assert.Empty(labelsWithInvalidCount);

        foreach (var label in new[] { "Organization", "Pipeline", "From", "To" })
            Assert.Single(cut.FindAll($"input[aria-label='{label}']"));

        Assert.Contains("aetheus-grid-fullheight", cut.Find(".analysis-table").ClassList);
    }

    [Fact]
    public void Portfolio_RendersProjectGradeEvenWhenThereAreNoDetailedReports()
    {
        _handler.SetPaginatedJsonResponse("api/projects",
        [
            new ProjectDto { Id = 1, Name = "Aetheus" }
        ]);
        _handler.SetJsonResponse("api/analysis/portfolio/projects",
        new List<AnalysisPortfolioProjectDto>
        {
            new AnalysisPortfolioProjectDto
            {
                ProjectId = 1,
                ProjectName = "Aetheus",
                OrganizationName = "Aetheus",
                ReportCount = 0,
                Grade = new AnalysisGradeSummaryDto { OverallGrade = AnalysisGrade.A }
            }
        });
        _handler.SetPaginatedJsonResponse("api/analysis/portfolio", Array.Empty<AnalysisPortfolioRowDto>());

        var cut = Render<AnalysisPortfolio>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("analysis-portfolio-project-card", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Aetheus", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("analysis-grade-a", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Portfolio_PrimesDetailedReportsOnFirstRender()
    {
        _handler.SetPaginatedJsonResponse("api/projects", Array.Empty<ProjectDto>());
        _handler.SetJsonResponse("api/analysis/portfolio/projects", new List<AnalysisPortfolioProjectDto>());
        _handler.SetPaginatedJsonResponse("api/analysis/portfolio",
        [
            new AnalysisPortfolioRowDto
            {
                AnalysisReportId = 42,
                ProjectId = 7,
                ProjectName = "Atlas",
                OrganizationName = "Aetheus",
                ScannerName = "SonarQube",
                Category = AnalysisCategory.CodeQuality,
                CompletedAt = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)
            }
        ]);

        var cut = Render<AnalysisPortfolio>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Atlas", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("SonarQube", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(_handler.Requests, request =>
                request.Method == HttpMethod.Get.Method
                && request.Url.Contains("api/analysis/portfolio?page=1&pageSize=25", StringComparison.Ordinal));
        });
    }
}
