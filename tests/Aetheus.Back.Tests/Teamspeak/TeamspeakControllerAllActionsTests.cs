// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Teamspeak;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class TeamspeakControllerAllActionsTests
{
    private readonly ITeamspeakService _service = Substitute.For<ITeamspeakService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly TeamspeakController _sut;

    public TeamspeakControllerAllActionsTests()
    {
        _sut = new TeamspeakController(_service, _authz);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    private void Allow(bool value) =>
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int>(),
            Arg.Any<Permission>(), Arg.Any<CancellationToken>()).Returns(value);

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.ExecuteAction(1, new TeamspeakActionRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetClientInfo_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.GetClientInfo(1, new TeamspeakClientInfoRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AllAuthorizedActions_ReturnOk()
    {
        Allow(true);
        _service.GetStateAsync(1, Arg.Any<CancellationToken>()).Returns(new TeamspeakDataDto());
        _service.GetClientsAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<TeamspeakClientDto>());
        _service.GetChannelsAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<TeamspeakChannelDto>());
        _service.GetBansAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<TeamspeakBanDto>());

        Assert.IsType<OkObjectResult>((await _sut.GetState(1, TestContext.Current.CancellationToken)).Result);
        Assert.IsType<OkObjectResult>((await _sut.GetClients(1, new PaginationRequest(), TestContext.Current.CancellationToken)).Result);
        Assert.IsType<OkObjectResult>((await _sut.GetChannels(1, new PaginationRequest(), TestContext.Current.CancellationToken)).Result);
        Assert.IsType<OkResult>(await _sut.ExecuteAction(1, new TeamspeakActionRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.Setup(1, new TeamspeakSetupRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.GetLogs(1, new TeamspeakLogRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.KickClient(1, new TeamspeakKickRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.BanClient(1, new TeamspeakBanRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.MoveClient(1, new TeamspeakMoveClientRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.PokeClient(1, new TeamspeakPokeClientRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkObjectResult>((await _sut.GetBans(1, new PaginationRequest(), TestContext.Current.CancellationToken)).Result);
        Assert.IsType<OkResult>(await _sut.Unban(1, 5, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.CreateChannel(1, new TeamspeakCreateChannelRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.EditChannel(1, 2, new TeamspeakEditChannelRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.DeleteChannel(1, 2, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.EditServer(1, new TeamspeakServerEditRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.SendGlobalMessage(1, new TeamspeakGlobalMessageRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.GetClientInfo(1, new TeamspeakClientInfoRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.GracefulRestart(1, new TeamspeakGracefulRestartRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.CreateSnapshot(1, new TeamspeakSnapshotCreateRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.DeploySnapshot(1, new TeamspeakSnapshotDeployRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.ListServerGroups(1, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.AddClientToServerGroup(1, new TeamspeakServerGroupAddRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.RemoveClientFromServerGroup(1, new TeamspeakServerGroupRemoveRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.ListTokens(1, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.CreateToken(1, new TeamspeakTokenCreateRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.DeleteToken(1, new TeamspeakTokenDeleteRequest(), TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.GetServerInfo(1, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.ListComplaints(1, TestContext.Current.CancellationToken));
        Assert.IsType<OkResult>(await _sut.DeleteComplaint(1, new TeamspeakComplaintDeleteRequest(), TestContext.Current.CancellationToken));

        // The Ok result alone could pass even if the controller skipped the service; pin the
        // controller→service wiring for the mutating actions so a regression is caught.
        await _service.Received(1).ExecuteActionAsync(1, Arg.Any<TeamspeakActionRequest>(), Arg.Any<CancellationToken>());
        await _service.Received(1).KickClientAsync(1, Arg.Any<TeamspeakKickRequest>(), Arg.Any<CancellationToken>());
        await _service.Received(1).BanClientAsync(1, Arg.Any<TeamspeakBanRequest>(), Arg.Any<CancellationToken>());
        await _service.Received(1).MoveClientAsync(1, Arg.Any<TeamspeakMoveClientRequest>(), Arg.Any<CancellationToken>());
        await _service.Received(1).CreateTokenAsync(1, Arg.Any<TeamspeakTokenCreateRequest>(), Arg.Any<CancellationToken>());
        await _service.Received(1).DeleteComplaintAsync(1, Arg.Any<TeamspeakComplaintDeleteRequest>(), Arg.Any<CancellationToken>());
    }
}
