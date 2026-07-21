// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Portsentry;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PortsentryServiceTests
{
    private readonly IPortsentryRepository _repo = Substitute.For<IPortsentryRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly Aetheus.Back.Components.Tasks.ITaskService _taskService = Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>();
    private readonly PortsentryService _sut;

    public PortsentryServiceTests()
    {
        _sut = new PortsentryService(_repo, _audit, _taskService);
    }

    [Fact]
    public async Task GetStateAsync_NoState_ReturnsEmpty()
    {
        _repo.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns((PortsentryState?)null);

        var result = await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsRunning);
        Assert.Empty(result.BlockedIps);
    }

    [Fact]
    public async Task GetStateAsync_WithState_ReturnsMappedDto()
    {
        _repo.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PortsentryState { ServerId = 1, IsRunning = true });
        var result = await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsRunning);
        Assert.Empty(result.BlockedIps);
        await _repo.DidNotReceive().GetBlockedIpsAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteActionAsync_CreatesShellTask()
    {
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.ExecuteActionAsync(1, new PortsentryActionRequest { Action = Aetheus.Shared.Enums.PortsentryAction.Start }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<string>(), "Portsentry", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _taskService.Received(1).NotifyTaskQueuedAsync(Arg.Any<ServerTask>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupAsync_DispatchesTypedSetupOperation_ModeAsTarget_PortsInEnv()
    {
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.SetupAsync(1, new PortsentrySetupRequest { Mode = "atcp", TcpPorts = "22,80", UdpPorts = "53" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(
            Arg.Is<ServerTask>(t => t.ServerId == 1
                && t.Executor == Aetheus.Shared.Enums.ExecutorType.Operation
                && t.Operation == Aetheus.Shared.Enums.OperationKind.PortsentrySetup
                && t.Command == "atcp"                 // scan mode carried in the task target
                && t.TimeoutSeconds == 120
                && t.EnvironmentVariables.Contains("22,80")
                && t.EnvironmentVariables.Contains("53")),
            Arg.Any<CancellationToken>());
        await _taskService.Received(1).NotifyTaskQueuedAsync(Arg.Any<ServerTask>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogsAsync_DispatchesTypedGetLogsOperation()
    {
        // NOTE: the requested line count is NOT consumed at this layer - GetLogsAsync dispatches a
        // typed OperationKind.PortsentryGetLogs task (target "-", timeout 15) and the agent owns the
        // log retrieval / line bounding. The [1,N] clamp the older test name implied is therefore not
        // observable here (see AUDIT_RESOLUTION_P1_Back.Tests.md). We assert the real contract instead:
        // a single Operation task of the correct kind, so a regression that mis-routes the call is caught.
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.GetLogsAsync(1, new PortsentryLogRequest { Lines = 99999 }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(
            Arg.Is<ServerTask>(t => t.ServerId == 1
                && t.Executor == Aetheus.Shared.Enums.ExecutorType.Operation
                && t.Operation == Aetheus.Shared.Enums.OperationKind.PortsentryGetLogs
                && t.TimeoutSeconds == 15),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnblockIpAsync_ValidIp_CreatesTask()
    {
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.UnblockIpAsync(1, new PortsentryUnblockRequest { IpAddress = "192.168.1.1" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<string>(), "Portsentry", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnblockIpAsync_InvalidIp_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UnblockIpAsync(1, new PortsentryUnblockRequest { IpAddress = "not-an-ip!" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatusAsync_CreatesTask()
    {
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.GetStatusAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWhitelistAsync_ReturnsMappedDtos()
    {
        _repo.GetWhitelistAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new PortsentryWhitelistIp { Id = 1, ServerId = 1, IpAddress = "10.0.0.1", Description = "test", CreatedAt = DateTime.UtcNow }
            ]);

        var result = await _sut.GetWhitelistAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("10.0.0.1", result[0].IpAddress);
    }

    [Fact]
    public async Task AddWhitelistIpAsync_ValidIp_AddsAndReturnsDto()
    {
        _repo.AddWhitelistIpAsync(Arg.Any<PortsentryWhitelistIp>(), Arg.Any<CancellationToken>())
            .Returns(new PortsentryWhitelistIp { Id = 1, ServerId = 1, IpAddress = "10.0.0.1", CreatedAt = DateTime.UtcNow });

        var result = await _sut.AddWhitelistIpAsync(1, new AddPortsentryWhitelistRequest { IpAddress = "10.0.0.1" }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("10.0.0.1", result.IpAddress);
        await _audit.Received(1).LogAsync(Arg.Any<string>(), "Portsentry", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddWhitelistIpAsync_InvalidIp_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AddWhitelistIpAsync(1, new AddPortsentryWhitelistRequest { IpAddress = "bad;ip" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveWhitelistIpAsync_Found_ReturnsTrueAndAudits()
    {
        _repo.RemoveWhitelistIpAsync(10, 1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RemoveWhitelistIpAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _audit.Received(1).LogAsync(Arg.Any<string>(), "Portsentry", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveWhitelistIpAsync_NotFound_ReturnsFalse()
    {
        _repo.RemoveWhitelistIpAsync(99, 1, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.RemoveWhitelistIpAsync(1, 99, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
        await _audit.DidNotReceive().LogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
