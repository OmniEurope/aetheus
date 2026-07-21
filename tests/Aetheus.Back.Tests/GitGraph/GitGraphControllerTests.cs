// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class GitGraphControllerTests
{
    private readonly IGitGraphService _serviceMock = Substitute.For<IGitGraphService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly GitGraphController _sut;

    public GitGraphControllerTests()
    {
        _sut = new GitGraphController(_serviceMock, _authzMock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
                }
            }
        };
    }

    [Fact]
    public async Task GetCommit_Authorized_ReturnsOk()
    {
        _serviceMock.GetCommitAsync(1, Arg.Any<CancellationToken>()).Returns(new GitCommitDto { Id = 1, ProjectId = 2, Sha = "abc" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 2, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.GetCommit(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCommit_Missing_ReturnsNotFound()
    {
        _serviceMock.GetCommitAsync(1, Arg.Any<CancellationToken>()).Returns((GitCommitDto?)null);

        var result = await _sut.GetCommit(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetCommit_NoAccess_ReturnsNotFound_NotForbid()
    {
        // Uniform 404 on no-access: avoid leaking existence (would be 403 vs 404).
        _serviceMock.GetCommitAsync(1, Arg.Any<CancellationToken>()).Returns(new GitCommitDto { Id = 1, ProjectId = 2, Sha = "abc" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 2, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetCommit(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetBranch_Authorized_ReturnsOk()
    {
        _serviceMock.GetBranchAsync(1, Arg.Any<CancellationToken>()).Returns(new GitBranchDto { Id = 1, ProjectId = 2, Name = "main" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 2, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.GetBranch(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetBranch_Missing_ReturnsNotFound()
    {
        _serviceMock.GetBranchAsync(1, Arg.Any<CancellationToken>()).Returns((GitBranchDto?)null);

        var result = await _sut.GetBranch(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetBranch_NoAccess_ReturnsNotFound_NotForbid()
    {
        _serviceMock.GetBranchAsync(1, Arg.Any<CancellationToken>()).Returns(new GitBranchDto { Id = 1, ProjectId = 2, Name = "main" });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 2, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetBranch(1, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
