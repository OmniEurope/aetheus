// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

public class PipelineArtifactServiceTrendTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly PipelineArtifactService _sut;

    public PipelineArtifactServiceTrendTests() =>
        _sut = new PipelineArtifactService(_repo, TimeProvider.System, NullLogger<PipelineArtifactService>.Instance);

    [Fact]
    public async Task GetCoverageTrendAsync_MapsRowsToDtos()
    {
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _repo.GetCoverageTrendAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new CoverageTrendRow(10, date, 0.85, 0.70), new CoverageTrendRow(11, date, 0.90, 0.80)]);

        var result = await _sut.GetCoverageTrendAsync(1, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.85, result[0].LineRate);
        Assert.Equal(11, result[1].RunId);
    }

    [Fact]
    public async Task GetComplexityTrendAsync_MapsRowsToDtos()
    {
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _repo.GetComplexityTrendAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ComplexityTrendRow(10, date, 3.5, 12, 8.2)]);

        var result = await _sut.GetComplexityTrendAsync(1, 25, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(3.5, result[0].AvgCyclomatic);
        Assert.Equal(12, result[0].MaxCyclomatic);
    }

    [Fact]
    public async Task GetProjectQualityTrendAsync_MapsAllThreeSeries()
    {
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _repo.GetProjectCoverageTrendAsync(7, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new CoverageTrendRow(10, date, 0.80, 0.60), new CoverageTrendRow(11, date, 0.85, 0.65)]);
        _repo.GetProjectComplexityTrendAsync(7, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ComplexityTrendRow(11, date, 4.0, 15, 9.1)]);
        _repo.GetProjectTestTrendAsync(7, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new TestTrendRow(10, date, 40, 2, 1), new TestTrendRow(11, date, 42, 0, 1)]);

        var result = await _sut.GetProjectQualityTrendAsync(7, 15, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Coverage.Count);
        Assert.Equal(0.85, result.Coverage[1].LineRate);
        Assert.Single(result.Complexity);
        Assert.Equal(9.1, result.Complexity[0].CrapAvg);
        Assert.Equal(2, result.Tests.Count);
        Assert.Equal(42, result.Tests[1].Passed);
    }

    [Fact]
    public async Task PublishCoverageAsync_RunNotFound_ReturnsNull()
    {
        _repo.GetPipelineRunWithPipelineAsync(9, Arg.Any<CancellationToken>()).Returns((PipelineRun?)null);

        var result = await _sut.PublishCoverageAsync(9, new PublishCoverageRequest { XmlContent = "<coverage/>" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task PublishCoverageAsync_InvalidXml_ReturnsNull()
    {
        _repo.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Pipeline = new Pipeline { Id = 1, Name = "CI" } });

        var result = await _sut.PublishCoverageAsync(1, new PublishCoverageRequest { XmlContent = "not xml at all" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task PublishCoverageAsync_ValidCobertura_StoresAndReturnsSummary()
    {
        _repo.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Pipeline = new Pipeline { Id = 1, Name = "CI" } });

        var xml = "<?xml version=\"1.0\"?><coverage line-rate=\"0.85\" branch-rate=\"0.7\" " +
                  "lines-covered=\"85\" lines-valid=\"100\" branches-covered=\"7\" branches-valid=\"10\"></coverage>";

        var result = await _sut.PublishCoverageAsync(1, new PublishCoverageRequest
        {
            XmlContent = xml,
            StageName = "Tests"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0.85, result.LineRate);
        await _repo.Received(1).AddCoverageResultAsync(Arg.Any<CoverageResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCoverageSummaryAsync_NoResults_ReturnsNull()
    {
        _repo.GetCoverageResultsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        Assert.Null(await _sut.GetCoverageSummaryAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCoverageSummaryAsync_ReturnsMostComprehensiveReportWithoutAssemblyPayload()
    {
        _repo.GetCoverageResultsAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new CoverageResult
                {
                    PipelineRunId = 1, LineRate = 0.9, BranchRate = 0.8, LinesValid = 10,
                    CreatedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc)
                },
                new CoverageResult
                {
                    PipelineRunId = 1, LineRate = 0.75, BranchRate = 0.7,
                    LinesCovered = 150, LinesValid = 200,
                    CreatedAt = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc),
                    FilesJson = "[{\"Assembly\":\"Aetheus.Back\",\"File\":\"Service.cs\",\"LineRate\":0.75,\"LinesCovered\":150,\"LinesValid\":200}]"
                }
            ]);

        var result = await _sut.GetCoverageSummaryAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0.75, result.LineRate);
        Assert.Empty(result.Assemblies);
    }

    [Fact]
    public async Task GetCoverageAssembliesAsync_PaginatesCanonicalReport()
    {
        _repo.GetCoverageResultsAsync(1, Arg.Any<CancellationToken>()).Returns(
        [
            new CoverageResult
            {
                PipelineRunId = 1,
                LinesValid = 300,
                FilesJson = "[{\"Assembly\":\"A\",\"File\":\"A.cs\",\"LinesCovered\":80,\"LinesValid\":100},"
                    + "{\"Assembly\":\"B\",\"File\":\"B.cs\",\"LinesCovered\":50,\"LinesValid\":100},"
                    + "{\"Assembly\":\"C\",\"File\":\"C.cs\",\"LinesCovered\":90,\"LinesValid\":100}]"
            }
        ]);

        var result = await _sut.GetCoverageAssembliesAsync(1, new PaginationRequest
        {
            Page = 2,
            PageSize = 2,
            SortBy = "LineRate"
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Single(result.Items);
        Assert.Equal("C", result.Items[0].Name);
    }

    [Fact]
    public async Task GetLintSummaryAsync_NoResults_ReturnsNull()
    {
        _repo.GetLintResultsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        Assert.Null(await _sut.GetLintSummaryAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLintSummaryAsync_ReturnsLatestSummary()
    {
        _repo.GetLintResultsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new LintResult { PipelineRunId = 1 }]);

        Assert.NotNull(await _sut.GetLintSummaryAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishLintAsync_RunNotFound_ReturnsNull()
    {
        _repo.GetPipelineRunWithPipelineAsync(9, Arg.Any<CancellationToken>()).Returns((PipelineRun?)null);
        Assert.Null(await _sut.PublishLintAsync(9, new PublishLintRequest { SarifContent = "{}" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishLintAsync_InvalidSarif_ReturnsNull()
    {
        _repo.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Pipeline = new Pipeline { Id = 1, Name = "CI" } });

        Assert.Null(await _sut.PublishLintAsync(1, new PublishLintRequest { SarifContent = "not valid sarif" }, ct: TestContext.Current.CancellationToken));
    }
}
