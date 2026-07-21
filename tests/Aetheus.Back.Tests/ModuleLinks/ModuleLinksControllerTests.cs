// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.ModuleLinks;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ModuleLinksControllerTests
{
    private readonly IModuleLinkService _serviceMock = Substitute.For<IModuleLinkService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly ModuleLinksController _sut;

    public ModuleLinksControllerTests()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new ModuleLinksController(_serviceMock, _authzMock);
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
    public async Task GetLinks_ReturnsOkWithList()
    {
        _serviceMock.GetLinksAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ModuleLinkDto { Id = 1, SourceType = ModuleLinkType.Docker, SourceIdentifier = "nginx" }]);

        var result = await _sut.GetLinks(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<ModuleLinkDto>)ok.Value!);
    }

    [Fact]
    public async Task GetLinksForResource_ReturnsOkWithList()
    {
        _serviceMock.GetLinksForResourceAsync(1, ModuleLinkType.Docker, "nginx", Arg.Any<CancellationToken>())
            .Returns([new LinkedResourceDto { LinkId = 1, Type = ModuleLinkType.Apache, Identifier = "test.com" }]);

        var result = await _sut.GetLinksForResource(1, ModuleLinkType.Docker, "nginx", TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<LinkedResourceDto>)ok.Value!);
    }

    [Fact]
    public async Task GetLinksPage_WithReadPermission_ReturnsPaginatedResult()
    {
        var request = new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["nginx"]
        };
        _serviceMock.GetLinksPageAsync(1, request, Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<LinkedResourceDto>
            {
                Items = [new LinkedResourceDto { LinkId = 1, Identifier = "site.conf" }],
                TotalCount = 1,
                Page = 1,
                PageSize = 25
            });

        var result = await _sut.GetLinksPage(1, request, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PaginatedResult<LinkedResourceDto>>(ok.Value);
        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task GetLinksPage_WithoutReadPermission_Forbids()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1,
                Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);
        var request = new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["nginx"]
        };

        var result = await _sut.GetLinksPage(1, request, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().GetLinksPageAsync(
            Arg.Any<int>(), Arg.Any<ModuleLinkPageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLink_ReturnsCreated()
    {
        var req = new CreateModuleLinkRequest { SourceType = ModuleLinkType.Docker, SourceIdentifier = "nginx", TargetType = ModuleLinkType.Apache, TargetIdentifier = "test.com" };
        _serviceMock.CreateLinkAsync(1, req, Arg.Any<CancellationToken>())
            .Returns(new ModuleLinkDto { Id = 1, SourceType = ModuleLinkType.Docker, SourceIdentifier = "nginx" });

        var result = await _sut.CreateLink(1, req, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteLink_ReturnsNoContent()
    {
        _serviceMock.GetServerIdForLinkAsync(1, Arg.Any<CancellationToken>()).Returns(1);

        var result = await _sut.DeleteLink(1, 1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
        await _serviceMock.Received(1).DeleteLinkAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoDetect_ReturnsNoContent()
    {
        var result = await _sut.AutoDetect(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).AutoDetectLinksAsync(1, Arg.Any<CancellationToken>());
    }

    // Negative-authorization coverage: deny the exact permission each endpoint checks (GetLinks=Read,
    // CreateLink=Write, DeleteLink=Admin) and assert Forbid + the service mutation never runs.

    [Fact]
    public async Task GetLinks_WithoutReadPermission_Forbids()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetLinks(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().GetLinksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLink_WithoutWritePermission_ForbidsAndDoesNotCreate()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);
        var req = new CreateModuleLinkRequest { SourceType = ModuleLinkType.Docker, SourceIdentifier = "nginx", TargetType = ModuleLinkType.Apache, TargetIdentifier = "test.com" };

        var result = await _sut.CreateLink(1, req, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().CreateLinkAsync(Arg.Any<int>(), Arg.Any<CreateModuleLinkRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteLink_WithoutAdminPermission_ForbidsAndDoesNotDelete()
    {
        // The link resolves to serverId 1; Admin is then denied on it.
        _serviceMock.GetServerIdForLinkAsync(1, Arg.Any<CancellationToken>()).Returns(1);
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.DeleteLink(1, 1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
        await _serviceMock.DidNotReceive().DeleteLinkAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
