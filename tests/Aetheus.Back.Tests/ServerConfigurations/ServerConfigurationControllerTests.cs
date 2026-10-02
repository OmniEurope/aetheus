// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ServerConfigurations;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServerConfigurationControllerTests
{
    private readonly IServerConfigurationService _serviceMock = Substitute.For<IServerConfigurationService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly ServerConfigurationController _sut;

    public ServerConfigurationControllerTests()
    {
        _authzMock.HasPermissionAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new ServerConfigurationController(_serviceMock, _authzMock);
    }

    [Fact]
    public async Task ExportConfiguration_Found_ReturnsYaml()
    {
        _serviceMock.ExportConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns("name: server1");

        var result = await _sut.ExportConfiguration(1, TestContext.Current.CancellationToken);

        var contentResult = Assert.IsType<ContentResult>(result.Result);
        Assert.Equal("name: server1", contentResult.Content);
        Assert.Equal("application/x-yaml", contentResult.ContentType);
    }

    [Fact]
    public async Task ExportConfiguration_NotFound_Returns404()
    {
        _serviceMock.ExportConfigurationAsync(99, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.ExportConfiguration(99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ValidateConfiguration_ReturnsOk()
    {
        var validation = new ServerConfigValidationResult { IsValid = true };
        _serviceMock.ValidateConfigurationAsync("yaml", Arg.Any<CancellationToken>()).Returns(validation);

        var result = await _sut.ValidateConfiguration(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ServerConfigValidationResult>(okResult.Value);
        Assert.True(dto.IsValid);
    }

    [Fact]
    public async Task PreviewImport_Found_ReturnsOk()
    {
        var preview = new ServerConfigPreviewDto();
        _serviceMock.PreviewImportAsync(1, "yaml", Arg.Any<CancellationToken>()).Returns(preview);

        var result = await _sut.PreviewImport(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task PreviewImport_NotFound_Returns404()
    {
        _serviceMock.PreviewImportAsync(99, "yaml", Arg.Any<CancellationToken>()).Returns((ServerConfigPreviewDto?)null);

        var result = await _sut.PreviewImport(99, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeployConfiguration_Found_ReturnsOk()
    {
        var deploy = new ServerConfigDeployResultDto();
        _serviceMock.DeployConfigurationAsync(1, "yaml", Arg.Any<CancellationToken>()).Returns(deploy);

        var result = await _sut.DeployConfiguration(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeployConfiguration_NotFound_Returns404()
    {
        _serviceMock.DeployConfigurationAsync(99, "yaml", Arg.Any<CancellationToken>()).Returns((ServerConfigDeployResultDto?)null);

        var result = await _sut.DeployConfiguration(99, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // Negative-authorization coverage: the ctor grants every permission, so these tests deny the exact
    // (ResourceType, Permission) each endpoint checks and assert Forbid + the service is never reached.
    // Export/Preview require Read, Validate requires Write, Deploy requires Admin.

    [Fact]
    public async Task ExportConfiguration_WithoutReadPermission_ForbidsAndDoesNotExport()
    {
        _authzMock.HasPermissionAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ExportConfiguration(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().ExportConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateConfiguration_WithoutWritePermission_ForbidsAndDoesNotValidate()
    {
        _authzMock.HasPermissionAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ValidateConfiguration(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().ValidateConfigurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewImport_WithoutReadPermission_ForbidsAndDoesNotPreview()
    {
        _authzMock.HasPermissionAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.PreviewImport(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().PreviewImportAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeployConfiguration_WithoutAdminPermission_ForbidsAndDoesNotDeploy()
    {
        _authzMock.HasPermissionAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.DeployConfiguration(1, new ServerConfigImportRequest { Yaml = "yaml" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().DeployConfigurationAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
