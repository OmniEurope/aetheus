// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PipelineApprovalServiceTests
{
    private readonly IPipelineRepository _repoMock = Substitute.For<IPipelineRepository>();
    private readonly IPipelineRunService _runServiceMock = Substitute.For<IPipelineRunService>();
    private readonly IHttpContextAccessor _httpContextAccessorMock = Substitute.For<IHttpContextAccessor>();
    private readonly IHubContext<PipelineHub> _hubMock = Substitute.For<IHubContext<PipelineHub>>();
    private readonly IClientProxy _clientProxyMock;
    private readonly PipelineApprovalService _sut;

    public PipelineApprovalServiceTests()
    {
        var clientProxyMock = Substitute.For<IClientProxy>();
        _clientProxyMock = clientProxyMock;
        clientProxyMock
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clientsMock = Substitute.For<IHubClients>();
        clientsMock.All.Returns(clientProxyMock);
        clientsMock.Group(Arg.Any<string>()).Returns(clientProxyMock);
        clientsMock.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxyMock);
        _hubMock.Clients.Returns(clientsMock);

        _sut = new PipelineApprovalService(
            _repoMock, _runServiceMock, _httpContextAccessorMock, _hubMock,
            NullLogger<PipelineApprovalService>.Instance, TimeProvider.System);
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
        _runServiceMock.ApplyRefusalAsync(10, Arg.Any<CancellationToken>()).Returns(PipelineStatus.Failed);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Rejected,
            Comments = "Not ready"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(ApprovalStatus.Rejected, result.Status);
        await _runServiceMock.Received(1).ApplyRefusalAsync(10, Arg.Any<CancellationToken>());
        await _runServiceMock.DidNotReceive().ResumeAfterApprovalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideApprovalAsync_TimedOut_FailsRunAndBroadcastsTimedOutReason()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.TimedOut, Arg.Any<DateTime>(), Arg.Any<int?>(), "Approval timed out", Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.TimedOut,
                ResolvedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
                Comments = "Approval timed out"
            });
        _runServiceMock.ApplyRefusalAsync(10, Arg.Any<CancellationToken>()).Returns(PipelineStatus.Failed);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.TimedOut,
            Comments = "Approval timed out"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(ApprovalStatus.TimedOut, result.Status);
        Assert.Equal("Approval timed out", result.Comments);
        Assert.NotNull(result.ResolvedAt);
        await _runServiceMock.Received(1).ApplyRefusalAsync(10, Arg.Any<CancellationToken>());
        await _runServiceMock.DidNotReceive().ResumeAfterApprovalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _clientProxyMock.Received(1).SendCoreAsync(
            "ApprovalResolved",
            Arg.Is<object?[]>(args => Equals(args[2], "TimedOut")),
            Arg.Any<CancellationToken>());
        await _clientProxyMock.Received(1).SendCoreAsync(
            "PipelineRunCompleted",
            Arg.Is<object?[]>(args => Equals(args[1], PipelineStatus.Failed)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// PLAN-003 2.7: a refused confirmation hands the run to its Rollback, so it has not completed yet;
    /// announcing a completion here would tell the page the run ended while the rollback runs.
    /// </summary>
    [Fact]
    public async Task DecideApprovalAsync_RefusedConfirmation_DoesNotAnnounceACompletedRun()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Rejected, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "Confirm",
                EnvironmentId = 1,
                Status = ApprovalStatus.Rejected,
                TimeoutMinutes = 10
            });
        _runServiceMock.ApplyRefusalAsync(10, Arg.Any<CancellationToken>()).Returns(PipelineStatus.Running);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Rejected
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _clientProxyMock.Received(1).SendCoreAsync(
            "ApprovalResolved", Arg.Is<object?[]>(args => Equals(args[2], "Rejected")), Arg.Any<CancellationToken>());
        await _clientProxyMock.DidNotReceive().SendCoreAsync(
            "PipelineRunCompleted", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideApprovalAsync_TimedOut_RunNoLongerWaiting_ReturnsNull()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.TimedOut, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.TimedOut
            });
        _runServiceMock.ApplyRefusalAsync(10, Arg.Any<CancellationToken>()).Returns((PipelineStatus?)null);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.TimedOut
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _clientProxyMock.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideApprovalAsync_ApprovedButResumeRefused_LeavesTheRunForTheReconciler()
    {
        // The approval row is resolved before the run is moved, so a refused resume strands the run in
        // WaitingForApproval with nothing pending on it: no decision offered on the page, and invisible
        // to the timeout sweep, which only reads pending rows. Run 2152 sat like that for eight days.
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Approved, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.Approved
            });
        _runServiceMock.ResumeAfterApprovalAsync(10, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Approved
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _runServiceMock.Received(1).ResumeAfterApprovalAsync(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideApprovalAsync_RejectedButTransitionRefused_LeavesTheRunForTheReconciler()
    {
        _repoMock.TryResolveApprovalAsync(
                1, ApprovalStatus.Rejected, Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval
            {
                Id = 1,
                PipelineRunId = 10,
                StageName = "deploy",
                EnvironmentId = 1,
                Status = ApprovalStatus.Rejected
            });
        _runServiceMock.ApplyRefusalAsync(10, Arg.Any<CancellationToken>()).Returns((PipelineStatus?)null);

        var result = await _sut.DecideApprovalAsync(1, new ApprovalDecisionRequest
        {
            Decision = ApprovalStatus.Rejected
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(PipelineStatus.Failed, ApprovalStatus.Approved)]
    [InlineData(PipelineStatus.Cancelled, ApprovalStatus.Rejected)]
    [InlineData(PipelineStatus.Success, ApprovalStatus.Approved)]
    public async Task DecideApprovalAsync_OnARunThatHasEnded_IsRefused_AndDecidesNothing(PipelineStatus runStatus, ApprovalStatus decision)
    {
        // PLAN-005 lot 3 / D34: run 2323 ended Cancelled with its Rollback approval still Pending.
        // Approving it would try to resume a finished run; the decision is refused instead.
        _repoMock.FindApprovalAsync(10, Arg.Any<CancellationToken>())
            .Returns(new PipelineApproval { Id = 10, PipelineRunId = 2323, StageName = "Rollback", Status = ApprovalStatus.Pending });
        _repoMock.GetRunStatusesByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PipelineStatus> { [2323] = runStatus });

        var refusal = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DecideApprovalAsync(10, new ApprovalDecisionRequest { Decision = decision }, TestContext.Current.CancellationToken));

        Assert.Contains("already ended", refusal.Message, StringComparison.Ordinal);
        await _repoMock.DidNotReceive().TryResolveApprovalAsync(
            Arg.Any<int>(), Arg.Any<ApprovalStatus>(), Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _runServiceMock.DidNotReceive().ResumeAfterApprovalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
