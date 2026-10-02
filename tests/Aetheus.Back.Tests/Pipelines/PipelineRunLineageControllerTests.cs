// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Audit of 2026-09-30: the lineage tile of a run lists the runs it started, which may belong to
/// pipelines the reader cannot open. Those are left out, so the tile names no pipeline, project or
/// build the caller could not read.
/// </summary>
public sealed class PipelineRunLineageControllerTests
{
    private readonly IPipelineRunService _runs = Substitute.For<IPipelineRunService>();
    private readonly IPipelineRunLineageService _lineage = Substitute.For<IPipelineRunLineageService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly PipelineRunLineageController _sut;

    public PipelineRunLineageControllerTests()
    {
        _sut = new PipelineRunLineageController(_runs, _lineage, _authz)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
                }
            }
        };
        _runs.GetPipelineIdForRunAsync(5, Arg.Any<CancellationToken>()).Returns(1);
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _lineage.GetLineageAsync(5, Arg.Any<CancellationToken>()).Returns(new PipelineRunLineageDto
        {
            Downstream =
            [
                new PipelineRunLinkDto { RunId = 6, PipelineId = 2, PipelineName = "readable-child" },
                new PipelineRunLinkDto { RunId = 7, PipelineId = 3, PipelineName = "hidden-child" }
            ]
        });
    }

    [Fact]
    public async Task ADownstreamRunOfAPipelineTheCallerCannotRead_IsLeftOut()
    {
        _authz.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, Permission.Read, Arg.Any<CancellationToken>())
            .Returns([1, 2]);

        var result = await _sut.GetRunLineage(5, TestContext.Current.CancellationToken);

        var lineage = Assert.IsType<PipelineRunLineageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["readable-child"], lineage.Downstream.Select(link => link.PipelineName));
    }

    [Fact]
    public async Task ACallerWhoReadsEveryPipeline_SeesEveryDownstreamRun()
    {
        _authz.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Pipeline, Permission.Read, Arg.Any<CancellationToken>())
            .Returns((List<int>?)null);

        var result = await _sut.GetRunLineage(5, TestContext.Current.CancellationToken);

        var lineage = Assert.IsType<PipelineRunLineageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, lineage.Downstream.Count);
    }
}
