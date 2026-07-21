// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Certbot;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class CertbotControllerTests
{
    private readonly ICertbotService _serviceMock = Substitute.For<ICertbotService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly CertbotController _sut;

    public CertbotControllerTests()
    {
        _sut = new CertbotController(_serviceMock, _authzMock);
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
    public async Task GetCertificates_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetCertificatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new CertbotCertificateDto { Name = "cert1" }]);

        var result = await _sut.GetCertificates(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<CertbotCertificateDto>)ok.Value!);
    }

    [Fact]
    public async Task GetCertificates_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetCertificates(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ExecuteAction_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.ExecuteAction(1, new CertbotActionRequest { Action = CertbotAction.Renew }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).ExecuteActionAsync(1, Arg.Any<CertbotActionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAction_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ExecuteAction(1, new CertbotActionRequest { Action = CertbotAction.Renew }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateCertificate_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.CreateCertificate(1, new CertbotCreateRequest { Domains = "example.com" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).CreateCertificateAsync(1, Arg.Any<CertbotCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCertificate_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.CreateCertificate(1, new CertbotCreateRequest { Domains = "example.com" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }
}
