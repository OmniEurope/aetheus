// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Cron;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Cron;

public class CronControllerTests
{
    private readonly ICronService _service = Substitute.For<ICronService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly CronController _sut;

    public CronControllerTests()
    {
        _sut = new CronController(_service, _authz)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Admin")],
                        "Bearer"))
                }
            }
        };
    }

    [Fact]
    public async Task SaveJob_Authorized_InvokesService()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new CronJobSaveRequest { User = "root", Schedule = "* * * * *", Command = "echo hi" };
        var result = await _sut.SaveJob(1, request, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _service.Received(1).SaveJobAsync(1, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveJob_Unauthorized_ReturnsForbid()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.SaveJob(1, new CronJobSaveRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteJob_Authorized_InvokesService()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new CronJobDeleteRequest { Id = "abc", User = "root" };
        var result = await _sut.DeleteJob(1, request, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _service.Received(1).DeleteJobAsync(1, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJob_Unauthorized_ReturnsForbid()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.DeleteJob(1, new CronJobDeleteRequest { Id = "x", User = "root" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }
}
