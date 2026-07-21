// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Apache;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ApacheControllerTests
{
    private readonly IApacheService _serviceMock = Substitute.For<IApacheService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly ApacheController _sut;

    public ApacheControllerTests()
    {
        _sut = new ApacheController(_serviceMock, _authzMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    [Fact]
    public async Task GetState_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ApacheDataDto());

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
    public async Task GetModules_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetModulesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ApacheModuleDto { Name = "mod_rewrite", IsEnabled = true }]);

        var result = await _sut.GetModules(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var modules = Assert.IsType<List<ApacheModuleDto>>(ok.Value);
        Assert.Single(modules);
    }

    [Fact]
    public async Task GetModules_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetModules(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetVirtualHosts_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetVirtualHostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.GetVirtualHosts(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetVirtualHosts_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetVirtualHosts(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ExecuteAction_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.ExecuteAction(1, new ApacheActionRequest { Action = ApacheAction.Restart }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).ExecuteActionAsync(1, Arg.Any<ApacheActionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ExecuteAction(1, new ApacheActionRequest { Action = ApacheAction.Restart }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetLogs_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.GetLogs(1, new ApacheLogRequest { LogType = "access", Lines = 100 }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetLogs_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetLogs(1, new ApacheLogRequest { LogType = "access", Lines = 100 }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetVHostConfig_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.GetVHostConfig(1, "example.com", TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetVHostConfig_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetVHostConfig(1, "example.com", TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SaveVHostConfig_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.SaveVHostConfig(1, "site.com", new ApacheVHostSaveRequest { SiteName = "ignored", Content = "config" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).SaveVHostConfigAsync(1, Arg.Is<ApacheVHostSaveRequest>(r => r.SiteName == "site.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveVHostConfig_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.SaveVHostConfig(1, "site.com", new ApacheVHostSaveRequest { SiteName = "site.com", Content = "c" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetHtaccess_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.GetHtaccess(1, "/var/www", TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetHtaccess_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetHtaccess(1, "/var/www", TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SaveHtaccess_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.SaveHtaccess(1, new ApacheHtaccessSaveRequest { DocumentRoot = "/var/www", Content = "c" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task SaveHtaccess_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.SaveHtaccess(1, new ApacheHtaccessSaveRequest { DocumentRoot = "/var/www", Content = "c" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }
}
