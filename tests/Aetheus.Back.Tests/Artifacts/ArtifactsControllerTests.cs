// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using System.Text;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Artifacts;

public class ArtifactsControllerTests
{
    private readonly IArtifactService _service = Substitute.For<IArtifactService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly ArtifactsController _sut;

    public ArtifactsControllerTests()
    {
        _sut = new ArtifactsController(_service, _authz);
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

    private static PipelineArtifactDto Artifact(int id = 1, int? projectId = 1) =>
        new() { Id = id, Name = "build", ProjectId = projectId };

    // --- GetProjectArtifacts ---

    [Fact]
    public async Task GetProjectArtifacts_Authorized_ReturnsOk()
    {
        Allow(true);
        _service.GetProjectArtifactsAsync(5, Arg.Any<ProjectArtifactsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<PipelineArtifactDto> { Items = [Artifact()], TotalCount = 1 });

        var result = await _sut.GetProjectArtifacts(5, new ProjectArtifactsRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetProjectArtifacts_Forbidden_ReturnsForbid()
    {
        Allow(false);

        var result = await _sut.GetProjectArtifacts(5, new ProjectArtifactsRequest(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // --- GetArtifact ---

    [Fact]
    public async Task GetArtifact_NotFound_ReturnsNotFound()
    {
        _service.GetArtifactAsync(9, Arg.Any<CancellationToken>()).Returns((PipelineArtifactDto?)null);

        var result = await _sut.GetArtifact(9, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetArtifact_NoProject_ReturnsOkWithoutAuthz()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact(projectId: null));

        var result = await _sut.GetArtifact(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetArtifact_ProjectNoPermission_ReturnsForbid()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(false);

        var result = await _sut.GetArtifact(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetArtifact_ProjectWithPermission_ReturnsOk()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(true);

        var result = await _sut.GetArtifact(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // --- DownloadArtifact ---

    [Fact]
    public async Task DownloadArtifact_NotFound_ReturnsNotFound()
    {
        _service.GetArtifactAsync(9, Arg.Any<CancellationToken>()).Returns((PipelineArtifactDto?)null);

        var result = await _sut.DownloadArtifact(9, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DownloadArtifact_StreamNull_ReturnsNotFound()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(true);
        _service.DownloadArtifactAsync(1, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await _sut.DownloadArtifact(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DownloadArtifact_Success_ReturnsFile()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(true);
        _service.DownloadArtifactAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("zip-bytes")));

        var result = await _sut.DownloadArtifact(1, TestContext.Current.CancellationToken);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("build.zip", file.FileDownloadName);
    }

    // --- PromoteArtifact ---

    [Fact]
    public async Task PromoteArtifact_NotFound_ReturnsNotFound()
    {
        _service.GetArtifactAsync(9, Arg.Any<CancellationToken>()).Returns((PipelineArtifactDto?)null);

        var result = await _sut.PromoteArtifact(9, new PromoteArtifactRequest { EnvironmentName = "prod" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PromoteArtifact_EmptyEnvironment_ReturnsBadRequest()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(true);

        var result = await _sut.PromoteArtifact(1, new PromoteArtifactRequest { EnvironmentName = "" }, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PromoteArtifact_Success_ReturnsOk()
    {
        _service.GetArtifactAsync(1, Arg.Any<CancellationToken>()).Returns(Artifact());
        Allow(true);
        _service.PromoteToEnvironmentAsync(1, "prod", Arg.Any<CancellationToken>())
            .Returns(Artifact());

        var result = await _sut.PromoteArtifact(1, new PromoteArtifactRequest { EnvironmentName = "prod" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // --- UploadArtifact ---

    [Fact]
    public async Task UploadArtifact_MissingServerIdClaim_ReturnsForbid()
    {
        var result = await _sut.UploadArtifact(1, "art", null, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UploadArtifact_NotAssignedToRun_ReturnsForbid()
    {
        SetAgentUser(serverId: 7);
        _service.IsAgentAssignedToRunAsync(1, 7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.UploadArtifact(1, "art", null, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UploadArtifact_InvalidName_ReturnsBadRequest()
    {
        SetAgentUser(serverId: 7);
        _service.IsAgentAssignedToRunAsync(1, 7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.UploadArtifact(1, "   ", null, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadArtifact_Success_ReturnsOk()
    {
        SetAgentUser(serverId: 7);
        _service.IsAgentAssignedToRunAsync(1, 7, Arg.Any<CancellationToken>()).Returns(true);
        _service.PublishArtifactAsync(1, "art", null, 7, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Artifact());

        var result = await _sut.UploadArtifact(1, "art", null, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadArtifact_MissingContentLength_ReturnsLengthRequired()
    {
        SetAgentUser(serverId: 7);
        _service.IsAgentAssignedToRunAsync(1, 7, Arg.Any<CancellationToken>()).Returns(true);
        _sut.ControllerContext.HttpContext.Request.ContentLength = null;

        var result = await _sut.UploadArtifact(1, "art", null, TestContext.Current.CancellationToken);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status411LengthRequired, status.StatusCode);
        await _service.DidNotReceive().PublishArtifactAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<long>(),
            Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    private void SetAgentUser(int serverId)
    {
        _sut.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("ServerId", serverId.ToString())], "agent"));
        _sut.ControllerContext.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        _sut.ControllerContext.HttpContext.Request.ContentLength = 7;
    }
}
