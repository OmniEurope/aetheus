// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Teamspeak;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class TeamspeakControllerTests
{
    private readonly ITeamspeakService _serviceMock = Substitute.For<ITeamspeakService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly TeamspeakController _sut;

    public TeamspeakControllerTests()
    {
        _sut = new TeamspeakController(_serviceMock, _authzMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    private void AllowRead(int sid) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, sid, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

    private void AllowWrite(int sid) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, sid, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

    private void AllowAdmin(int sid) =>
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, sid, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);

    [Fact]
    public async Task GetState_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetStateAsync(1, Arg.Any<CancellationToken>()).Returns(new TeamspeakDataDto());
        var result = await _sut.GetState(1, TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetState_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);
        var result = await _sut.GetState(1, TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ExecuteAction_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.ExecuteActionAsync(1, Arg.Any<TeamspeakActionRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.ExecuteAction(1, new TeamspeakActionRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);
        var result = await _sut.ExecuteAction(1, new TeamspeakActionRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Setup_Authorized_ReturnsOk()
    {
        AllowAdmin(1);
        _serviceMock.SetupAsync(1, Arg.Any<TeamspeakSetupRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.Setup(1, new TeamspeakSetupRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Setup_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);
        var result = await _sut.Setup(1, new TeamspeakSetupRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetLogs_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetLogsAsync(1, Arg.Any<TeamspeakLogRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.GetLogs(1, new TeamspeakLogRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task KickClient_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.KickClientAsync(1, Arg.Any<TeamspeakKickRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.KickClient(1, new TeamspeakKickRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task BanClient_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.BanClientAsync(1, Arg.Any<TeamspeakBanRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.BanClient(1, new TeamspeakBanRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetBans_Authorized_ReturnsOk()
    {
        AllowRead(1);
        _serviceMock.GetBansAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<TeamspeakBanDto>());
        var result = await _sut.GetBans(1, new PaginationRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Unban_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.UnbanAsync(1, 5, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.Unban(1, 5, TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CreateChannel_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.CreateChannelAsync(1, Arg.Any<TeamspeakCreateChannelRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.CreateChannel(1, new TeamspeakCreateChannelRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task EditChannel_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.EditChannelAsync(1, Arg.Any<TeamspeakEditChannelRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.EditChannel(1, 5, new TeamspeakEditChannelRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteChannel_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.DeleteChannelAsync(1, 5, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.DeleteChannel(1, 5, TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task EditServer_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.EditServerAsync(1, Arg.Any<TeamspeakServerEditRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.EditServer(1, new TeamspeakServerEditRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task SendGlobalMessage_Authorized_ReturnsOk()
    {
        AllowWrite(1);
        _serviceMock.SendGlobalMessageAsync(1, Arg.Any<TeamspeakGlobalMessageRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result = await _sut.SendGlobalMessage(1, new TeamspeakGlobalMessageRequest(), TestContext.Current.CancellationToken);
        Assert.IsType<OkResult>(result);
    }
}
