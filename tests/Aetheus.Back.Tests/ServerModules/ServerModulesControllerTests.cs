// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.ServerModules;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServerModulesControllerTests
{
    private readonly IServerModuleService _serviceMock = Substitute.For<IServerModuleService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly ServerModulesController _sut;

    public ServerModulesControllerTests()
    {
        _sut = new ServerModulesController(_serviceMock, _authzMock);
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
    public async Task GetModules_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetByServerIdAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ServerModuleDto { Id = 1, Name = "Docker" }]);

        var result = await _sut.GetModules(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<ServerModuleDto>)ok.Value!);
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
    public async Task GetModule_Found_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetByIdAsync(1, 5, Arg.Any<CancellationToken>())
            .Returns(new ServerModuleDto { Id = 5, Name = "Apache" });

        var result = await _sut.GetModule(1, 5, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetModule_NotFound_ReturnsNotFound()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetByIdAsync(1, 999, Arg.Any<CancellationToken>())
            .Returns((ServerModuleDto?)null);

        var result = await _sut.GetModule(1, 999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateModule_Authorized_ReturnsCreated()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.CreateAsync(1, Arg.Any<CreateServerModuleRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ServerModuleDto { Id = 1, Name = "New" });

        var result = await _sut.CreateModule(1, new CreateServerModuleRequest { Name = "New" }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreateModule_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.CreateModule(1, new CreateServerModuleRequest { Name = "X" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UpdateModule_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.UpdateAsync(1, 5, Arg.Any<UpdateServerModuleRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ServerModuleDto { Id = 5, Name = "Updated" });

        var result = await _sut.UpdateModule(1, 5, new UpdateServerModuleRequest { Name = "Updated" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteModule_Authorized_ReturnsNoContent()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.DeleteAsync(1, 5, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteModule(1, 5, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteModule_NotFound_ReturnsNotFound()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.DeleteAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteModule(1, 5, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
