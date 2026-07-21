// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Rkhunter;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Rkhunter;

public class RkhunterControllerTests
{
    private readonly IRkhunterService _service = Substitute.For<IRkhunterService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly RkhunterController _sut;

    public RkhunterControllerTests()
    {
        _sut = new RkhunterController(_service, _authz);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    private void Allow(bool value) =>
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int>(),
            Arg.Any<Permission>(), Arg.Any<CancellationToken>()).Returns(value);

    [Fact]
    public async Task GetState_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>((await _sut.GetState(1, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task GetState_Authorized_ReturnsOk()
    {
        Allow(true);
        _service.GetStateAsync(1, Arg.Any<CancellationToken>()).Returns(new RkhunterDataDto());

        Assert.IsType<OkObjectResult>((await _sut.GetState(1, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.ExecuteAction(1, new RkhunterActionRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAction_Authorized_CallsServiceAndReturnsOk()
    {
        Allow(true);
        var result = await _sut.ExecuteAction(1, new RkhunterActionRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _service.Received(1).ExecuteActionAsync(1, Arg.Any<RkhunterActionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Setup_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.Setup(1, new RkhunterSetupRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Setup_Authorized_ReturnsOk()
    {
        Allow(true);
        Assert.IsType<OkResult>(await _sut.Setup(1, new RkhunterSetupRequest(), TestContext.Current.CancellationToken));
        await _service.Received(1).SetupAsync(1, Arg.Any<RkhunterSetupRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogs_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.GetLogs(1, new RkhunterLogRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLogs_Authorized_ReturnsOk()
    {
        Allow(true);
        Assert.IsType<OkResult>(await _sut.GetLogs(1, new RkhunterLogRequest(), TestContext.Current.CancellationToken));
        await _service.Received(1).GetLogsAsync(1, Arg.Any<RkhunterLogRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWarnings_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>((await _sut.GetWarnings(1, false, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task GetWarnings_Authorized_ReturnsOk()
    {
        Allow(true);
        _service.GetWarningsAsync(1, false, Arg.Any<CancellationToken>()).Returns([]);

        Assert.IsType<OkObjectResult>((await _sut.GetWarnings(1, false, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task GetScanHistory_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>((await _sut.GetScanHistory(1, 50, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task GetScanHistory_Authorized_ReturnsOk()
    {
        Allow(true);
        _service.GetScanHistoryAsync(1, 50, Arg.Any<CancellationToken>()).Returns([]);

        Assert.IsType<OkObjectResult>((await _sut.GetScanHistory(1, 50, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task SetSchedule_Forbidden_ReturnsForbid()
    {
        Allow(false);
        Assert.IsType<ForbidResult>(await _sut.SetSchedule(1, new RkhunterScheduleRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetSchedule_Authorized_ReturnsOk()
    {
        Allow(true);
        Assert.IsType<OkResult>(await _sut.SetSchedule(1, new RkhunterScheduleRequest(), TestContext.Current.CancellationToken));
        await _service.Received(1).SetScheduleAsync(1, Arg.Any<RkhunterScheduleRequest>(), Arg.Any<CancellationToken>());
    }
}
