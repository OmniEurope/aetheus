// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class PipelineFavoritesControllerTests
{
    private readonly IPipelineFavoriteService _service = Substitute.For<IPipelineFavoriteService>();
    private readonly IResourceAuthorizationService _authorization = Substitute.For<IResourceAuthorizationService>();
    private readonly PipelineFavoritesController _controller;

    public PipelineFavoritesControllerTests()
    {
        _controller = new PipelineFavoritesController(_service, _authorization)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "alice")], "test"))
                }
            }
        };
    }

    [Fact]
    public async Task GetFavorites_ReturnsOnlyAccessibleFavorites()
    {
        _authorization.GetAccessibleResourceIdsAsync(
                Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, Permission.Read,
                TestContext.Current.CancellationToken)
            .Returns([2, 3]);
        _service.GetFavoritesAsync(
                Arg.Is<List<int>?>(ids => ids != null && ids.SequenceEqual(new[] { 2, 3 })),
                TestContext.Current.CancellationToken)
            .Returns(new PipelineFavoritesDto { PipelineIds = [2] });

        var response = await _controller.GetFavorites(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal([2], Assert.IsType<PipelineFavoritesDto>(ok.Value).PipelineIds);
    }

    [Fact]
    public async Task SetFavorite_RequiresPipelineReadPermission()
    {
        _authorization.HasPermissionAsync(
                Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, 7, Permission.Read,
                TestContext.Current.CancellationToken)
            .Returns(false);

        var response = await _controller.SetFavorite(
            7, new SetPipelineFavoriteRequest { IsFavorite = true },
            TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(response.Result);
        await _service.DidNotReceive().SetFavoriteAsync(
            Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFavorite_ReturnsPersistedState()
    {
        _authorization.HasPermissionAsync(
                Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, 7, Permission.Read,
                TestContext.Current.CancellationToken)
            .Returns(true);
        _service.SetFavoriteAsync(7, true, TestContext.Current.CancellationToken)
            .Returns(new PipelineFavoriteDto { PipelineId = 7, IsFavorite = true });

        var response = await _controller.SetFavorite(
            7, new SetPipelineFavoriteRequest { IsFavorite = true },
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.True(Assert.IsType<PipelineFavoriteDto>(ok.Value).IsFavorite);
    }
}
