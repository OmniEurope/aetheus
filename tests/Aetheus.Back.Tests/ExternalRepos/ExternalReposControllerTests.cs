// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ExternalReposControllerTests
{
    private readonly IExternalRepoService _service = Substitute.For<IExternalRepoService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly ExternalReposController _sut;

    public ExternalReposControllerTests()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new ExternalReposController(_service, _authz);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    [Fact]
    public void IsFeatureEnabled_ReflectsService()
    {
        _service.IsEnabled.Returns(true);
        var result = _sut.IsFeatureEnabled();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)ok.Value!);
    }

    [Fact]
    public async Task GetForProject_FeatureDisabled_ReturnsNotFound()
    {
        _service.IsEnabled.Returns(false);
        var result = await _sut.GetForProject(1, TestContext.Current.CancellationToken);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Attach_Enabled_ReturnsOk()
    {
        _service.IsEnabled.Returns(true);
        _service.AttachAsync(Arg.Any<AttachExternalRepoRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExternalRepoDto { ProjectId = 1, OwnerOrGroup = "acme", RepositoryName = "demo" });

        var result = await _sut.Attach(new AttachExternalRepoRequest { ProjectId = 1, OwnerOrGroup = "acme", RepositoryName = "demo" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Attach_Disabled_ReturnsNotFound()
    {
        _service.IsEnabled.Returns(false);

        var result = await _sut.Attach(new AttachExternalRepoRequest { ProjectId = 1, OwnerOrGroup = "a", RepositoryName = "b" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
