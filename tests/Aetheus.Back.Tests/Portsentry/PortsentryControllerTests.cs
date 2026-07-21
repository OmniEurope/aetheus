// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Portsentry;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PortsentryControllerTests
{
    private readonly IPortsentryService _serviceMock = Substitute.For<IPortsentryService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly PortsentryController _sut;

    public PortsentryControllerTests()
    {
        _sut = new PortsentryController(_serviceMock, _authzMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    private void AllowRead(int serverId) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, serverId, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

    private void AllowWrite(int serverId) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, serverId, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

    private void AllowAdmin(int serverId) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, serverId, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);

    private void DenyAll() { }

    [Fact]
    public async Task GetState_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PortsentryDataDto());

        var result = await _sut.GetState(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetState_Forbidden_ReturnsForbid()
    {
        DenyAll();
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetState(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ExecuteAction_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.ExecuteActionAsync(1, Arg.Any<PortsentryActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.ExecuteAction(1, new PortsentryActionRequest { Action = PortsentryAction.Start }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ExecuteAction(1, new PortsentryActionRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Setup_Authorized_ReturnsOk()
    {
        AllowAdmin(1);
        _serviceMock.SetupAsync(1, Arg.Any<PortsentrySetupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.Setup(1, new PortsentrySetupRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Setup_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Setup(1, new PortsentrySetupRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetLogs_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetLogsAsync(1, Arg.Any<PortsentryLogRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.GetLogs(1, new PortsentryLogRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task UnblockIp_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.UnblockIpAsync(1, Arg.Any<PortsentryUnblockRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.UnblockIp(1, new PortsentryUnblockRequest { IpAddress = "1.2.3.4" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetStatus_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetStatusAsync(1, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.GetStatus(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetWhitelist_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetWhitelistAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<PortsentryWhitelistIpDto>());

        var result = await _sut.GetWhitelist(1, new PaginationRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddWhitelistIp_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.AddWhitelistIpAsync(1, Arg.Any<AddPortsentryWhitelistRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PortsentryWhitelistIpDto());

        var result = await _sut.AddWhitelistIp(1, new AddPortsentryWhitelistRequest { IpAddress = "10.0.0.1" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveWhitelistIp_Found_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.RemoveWhitelistIpAsync(1, 10, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RemoveWhitelistIp(1, 10, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RemoveWhitelistIp_NotFound_ReturnsNotFound()
    {
        AllowWrite(1);
        _serviceMock.RemoveWhitelistIpAsync(1, 99, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.RemoveWhitelistIp(1, 99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
