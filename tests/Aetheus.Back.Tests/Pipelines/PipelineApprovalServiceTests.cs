// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PipelineApprovalServiceTests
{
    private readonly IPipelineRepository _repoMock = Substitute.For<IPipelineRepository>();
    private readonly IPipelineRunService _runServiceMock = Substitute.For<IPipelineRunService>();
    private readonly IHttpContextAccessor _httpContextAccessorMock = Substitute.For<IHttpContextAccessor>();
    private readonly IHubContext<PipelineHub> _hubMock = Substitute.For<IHubContext<PipelineHub>>();
    private readonly PipelineApprovalService _sut;

    public PipelineApprovalServiceTests()
    {
        var clientProxyMock = Substitute.For<IClientProxy>();
        clientProxyMock
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clientsMock = Substitute.For<IHubClients>();
        clientsMock.All.Returns(clientProxyMock);
        clientsMock.Group(Arg.Any<string>()).Returns(clientProxyMock);
        clientsMock.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxyMock);
        _hubMock.Clients.Returns(clientsMock);

        _sut = new PipelineApprovalService(_repoMock, _runServiceMock, _httpContextAccessorMock, _hubMock, TimeProvider.System);
    }

    [Fact]
    public async Task GetApprovalsAsync_ReturnsMappedList()
    {
        _repoMock.GetApprovalsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineApproval
            {
                Id = 1, PipelineRunId = 1, StageName = "deploy",
                EnvironmentId = 1, Status = ApprovalStatus.Pending
            }]);

        var result = await _sut.GetApprovalsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("deploy", result[0].StageName);
        Assert.Equal(ApprovalStatus.Pending, result[0].Status);
    }

    [Fact]
    public async Task DecideApprovalAsync_InvalidDecision_ReturnsNull()
    {
        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Pending
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DecideApprovalAsync_ApprovalNotFound_ReturnsNull()
    {
        _repoMock.TryResolveApprovalAsync(
                99, ApprovalStatus.Approved, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((PipelineApproval?)null);

        var result = await _sut.DecideApprovalAsync(99, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Approved
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DecideApprovalAsync_AlreadyResolved_ReturnsNull()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Approved, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((PipelineApproval?)null);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Approved
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DecideApprovalAsync_Approved_ResumesRunAndNotifies()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Approved, Arg.Any<DateTime>(), Arg.Any<int?>(), "LGTM", Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.Approved,
                Comments = "LGTM"
            });
        _runServiceMock.ResumeAfterApprovalAsync(10, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Approved,
            Comments = "LGTM"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(ApprovalStatus.Approved, result.Status);
        Assert.Equal("LGTM", result.Comments);
        await _runServiceMock.Received(1).ResumeAfterApprovalAsync(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideApprovalAsync_Rejected_FailsRunAndNotifies()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Rejected, Arg.Any<DateTime>(), Arg.Any<int?>(), "Not ready", Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.Rejected,
                Comments = "Not ready"
            });
        _repoMock.TryTransitionPipelineRunStatusAsync(
                10, PipelineStatus.WaitingForApproval, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Rejected,
            Comments = "Not ready"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(ApprovalStatus.Rejected, result.Status);
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            10, PipelineStatus.WaitingForApproval, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        await _runServiceMock.DidNotReceive().ResumeAfterApprovalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
