// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class AnalysisGateOperationExecutorTests
{
    [Theory]
    [InlineData(AnalysisGateStatus.Passed, 0)]
    [InlineData(AnalysisGateStatus.Warning, 0)]
    [InlineData(AnalysisGateStatus.Blocked, 0)]
    [InlineData(AnalysisGateStatus.Error, 1)]
    public async Task ExecuteAsync_PublishesBothSummariesAndEnforcesAggregateVerdict(
        AnalysisGateStatus status,
        int expectedExitCode)
    {
        var api = Substitute.For<IServerApiClient>();
        api.GetAnalysisRunGateAsync(42, "security", Arg.Any<CancellationToken>()).Returns(new AnalysisRunGateDto
        {
            PipelineRunId = 42,
            Status = status,
            ReportCount = 1,
            Reports = [new AnalysisRunGateReportDto { ReportId = 7, ScannerName = "Scanner", ScannerVersion = "1" }],
            Findings = [new AnalysisRunGateFindingDto { FindingId = 9, RuleId = "R1", Title = "Finding", Severity = AnalysisSeverity.High, IsNew = true }]
        });
        api.UploadArtifactAsync(42, Arg.Any<string>(), "Gate", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifactDto { Id = 1 });
        var executor = new AnalysisGateOperationExecutor(api);

        var result = await executor.ExecuteAsync(OperationKind.PipelineEvaluateAnalysisGate, "security",
            new Dictionary<string, string> { ["AETHEUS_RUN_ID"] = "42", ["AETHEUS_STAGE_NAME"] = "Gate" },
            60, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(expectedExitCode, result.ExitCode);
        await api.Received(1).GetAnalysisRunGateAsync(
            42, "security", Arg.Any<CancellationToken>());
        await api.Received(2).UploadArtifactAsync(42, Arg.Any<string>(), "Gate", Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }
}
