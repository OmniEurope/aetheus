// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using System.Text;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Releases;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ReleasesControllerTests
{
    private readonly IReleaseService _serviceMock = Substitute.For<IReleaseService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly IConfiguration _configMock = Substitute.For<IConfiguration>();
    private readonly IPipelineRunService _pipelineRunService = Substitute.For<IPipelineRunService>();
    private readonly ReleasesController _sut;

    public ReleasesControllerTests()
    {
        _sut = new ReleasesController(_serviceMock, _authzMock, _configMock, _pipelineRunService);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("ServerId", "7")], "test"))
            }
        };
    }

    [Fact]
    public async Task CreateRelease_AssignedAgentAndMatchingProject_CreatesRelease()
    {
        _pipelineRunService.IsServerAssignedToRunAsync(42, 7, Arg.Any<CancellationToken>()).Returns(true);
        _pipelineRunService.GetRunPipelineContextAsync(42, Arg.Any<CancellationToken>()).Returns((5, 3));
        _serviceMock.CreateReleaseFromPipelineAsync(
                3, 42, "1.2.3", null, null, null, null, null, true, Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 9, ProjectId = 3, Version = "1.2.3" });

        var result = await _sut.CreateRelease(
            new CreateReleaseRequest { PipelineRunId = 42, Version = "1.2.3", Deployed = true }, 3,
            TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreateRelease_AgentNotAssignedToRun_ReturnsForbid()
    {
        _pipelineRunService.IsServerAssignedToRunAsync(42, 7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.CreateRelease(
            new CreateReleaseRequest { PipelineRunId = 42, Version = "1.2.3" }, 3,
            TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceiveWithAnyArgs().CreateReleaseFromPipelineAsync(
            default, default, default!, default, default, default, default, default, default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreateRelease_RunBelongsToDifferentProject_ReturnsForbid()
    {
        _pipelineRunService.IsServerAssignedToRunAsync(42, 7, Arg.Any<CancellationToken>()).Returns(true);
        _pipelineRunService.GetRunPipelineContextAsync(42, Arg.Any<CancellationToken>()).Returns((5, 99));

        var result = await _sut.CreateRelease(
            new CreateReleaseRequest { PipelineRunId = 42, Version = "1.2.3" }, 3,
            TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceiveWithAnyArgs().CreateReleaseFromPipelineAsync(
            default, default, default!, default, default, default, default, default, default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreateRelease_WithoutRunId_ReturnsBadRequest()
    {
        var result = await _sut.CreateRelease(
            new CreateReleaseRequest { Version = "1.2.3" }, 3, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await _serviceMock.DidNotReceiveWithAnyArgs().CreateReleaseFromPipelineAsync(
            default, default, default!, default, default, default, default, default, default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetReleases_ReturnsOk()
    {
        _authzMock.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(new List<int> { 1 });
        _serviceMock.GetReleasesAsync(null, Arg.Any<PaginationRequest>(), Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<ReleaseDto> { Items = [new ReleaseDto { Id = 1, Version = "1.0.0" }], TotalCount = 1 });

        var result = await _sut.GetReleases(null, new PaginationRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetReleases_NoAccessible_ReturnsEmptyOk()
    {
        _authzMock.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(new List<int>());

        var result = await _sut.GetReleases(null, new PaginationRequest(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PaginatedResult<ReleaseDto>>(ok.Value);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetRelease_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 1, Version = "1.0.0" });

        var result = await _sut.GetRelease(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetRelease_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetRelease(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetRelease_NotFound_ReturnsNotFound()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns((ReleaseDto?)null);

        var result = await _sut.GetRelease(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SyncReleases_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.SyncReleasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ReleaseDto { Id = 1, Version = "1.0.0" }]);

        var result = await _sut.SyncReleases(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SyncReleases_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.SyncReleases(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task TriggerBuild_Authorized_ReturnsAccepted()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.TriggerReleaseBuildAsync(1, Arg.Any<TriggerReleaseBuildRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 1, Version = "1.0.0" });

        var result = await _sut.TriggerBuild(1, new TriggerReleaseBuildRequest { PipelineId = 10 }, TestContext.Current.CancellationToken);

        Assert.IsType<AcceptedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Rollback_Authorized_ReturnsAccepted()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 1, ProjectId = 7, Version = "1.0.0" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 7, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, 5, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.RollbackReleaseAsync(1, Arg.Any<RollbackReleaseRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReleaseRollbackDto { Id = 1, SourceReleaseId = 1, TargetReleaseId = 0 });

        var result = await _sut.Rollback(1, new RollbackReleaseRequest { PipelineId = 5 }, TestContext.Current.CancellationToken);

        Assert.IsType<AcceptedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Rollback_WithoutProjectWrite_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 1, ProjectId = 7, Version = "1.0.0" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 7, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Rollback(1, new RollbackReleaseRequest { PipelineId = 5 }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().RollbackReleaseAsync(Arg.Any<int>(), Arg.Any<RollbackReleaseRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Promote_Authorized_ReturnsAccepted()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Release, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.PromoteReleaseAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ReleaseDto { Id = 1, Version = "1.0.0" });

        var result = await _sut.Promote(1, TestContext.Current.CancellationToken);

        Assert.IsType<AcceptedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Webhook_NoSecret_Returns503()
    {
        _configMock["Webhook:Secret"].Returns((string?)null);

        var result = await _sut.Webhook(TestContext.Current.CancellationToken);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_ReturnsUnauthorized()
    {
        _configMock["Webhook:Secret"].Returns("secret123");
        _serviceMock.ValidateWebhookSignature(Arg.Any<string?>(), "secret123", Arg.Any<string>())
            .Returns(false);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"ref\":\"refs/tags/v1.0.0\"}"));
        httpContext.Request.Headers["X-Hub-Signature-256"] = "sha256=invalid";
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _sut.Webhook(TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Webhook_ValidPayload_ReturnsOk()
    {
        _configMock["Webhook:Secret"].Returns("secret123");
        _serviceMock.ValidateWebhookSignature(Arg.Any<string?>(), "secret123", Arg.Any<string>())
            .Returns(true);

        var body = "{\"ref\":\"refs/tags/v1.0.0\",\"repositoryUrl\":\"https://github.com/test\"}";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Headers["X-Hub-Signature-256"] = "sha256=valid";
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _sut.Webhook(TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).HandleWebhookAsync(Arg.Any<WebhookPayload>(), Arg.Any<CancellationToken>());
    }

    // --- GetServerReleases (relocated from ServersController, route unchanged) ---

    [Fact]
    public async Task GetServerReleases_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetServerReleasesAsync(1, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<ReleaseDto>());

        var result = await _sut.GetServerReleases(1, new PaginationRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// The endpoint changed controller, not resource: it is still the SERVER's Read permission that
    /// gates it. Authorizing against the release scope instead would silently widen access.
    /// </summary>
    [Fact]
    public async Task GetServerReleases_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetServerReleases(1, new PaginationRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
