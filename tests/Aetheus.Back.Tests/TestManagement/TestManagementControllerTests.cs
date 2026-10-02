// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.TestManagement;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class TestManagementControllerTests
{
    private readonly ITestManagementService _serviceMock = Substitute.For<ITestManagementService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly IPipelineRunService _pipelineRunServiceMock = Substitute.For<IPipelineRunService>();
    private readonly TestManagementController _sut;

    public TestManagementControllerTests()
    {
        _authzMock.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns((List<int>?)null);
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new TestManagementController(_serviceMock, _authzMock, _pipelineRunServiceMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("ServerId", "1")], "test"))
            }
        };
    }

    [Fact]
    public async Task GetSuites_ReturnsOk()
    {
        _serviceMock.GetSuitesAsync(null, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns([new TestSuiteDto { Id = 1, Name = "Suite1" }]);

        var result = await _sut.GetSuites(null, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<TestSuiteDto>)ok.Value!);
    }

    [Fact]
    public async Task GetSuite_Found_ReturnsOk()
    {
        _serviceMock.GetSuiteDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TestSuiteDetailDto { Id = 1, Name = "Suite1" });

        var result = await _sut.GetSuite(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetSuite_NotFound_ReturnsNotFound()
    {
        _serviceMock.GetSuiteDetailAsync(999, Arg.Any<CancellationToken>())
            .Returns((TestSuiteDetailDto?)null);

        var result = await _sut.GetSuite(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateSuite_ReturnsCreated()
    {
        _serviceMock.CreateSuiteAsync(Arg.Any<CreateTestSuiteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestSuiteDto { Id = 1, Name = "New" });

        var result = await _sut.CreateSuite(new CreateTestSuiteRequest { Name = "New", ProjectId = 1 }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSuite_Found_ReturnsOk()
    {
        _serviceMock.UpdateSuiteAsync(1, Arg.Any<UpdateTestSuiteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestSuiteDto { Id = 1, Name = "Updated" });

        var result = await _sut.UpdateSuite(1, new UpdateTestSuiteRequest { Name = "Updated" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSuite_NotFound_ReturnsNotFound()
    {
        _serviceMock.UpdateSuiteAsync(999, Arg.Any<UpdateTestSuiteRequest>(), Arg.Any<CancellationToken>())
            .Returns((TestSuiteDto?)null);

        var result = await _sut.UpdateSuite(999, new UpdateTestSuiteRequest { Name = "X" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteSuite_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteSuiteAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteSuite(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteSuite_NotFound_ReturnsNotFound()
    {
        _serviceMock.DeleteSuiteAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteSuite(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task IngestResults_ReturnsOk()
    {
        _serviceMock.IngestTestResultsAsync(Arg.Any<IngestTestResultsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestIngestionResultDto { SuiteId = 1, TotalCases = 10, NewCases = 5 });

        _pipelineRunServiceMock.IsServerAssignedToRunAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _pipelineRunServiceMock.GetRunAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRunDto { Id = 1, ProjectId = 1 });

        var result = await _sut.IngestResults(new IngestTestResultsRequest { ProjectId = 1, PipelineRunId = 1, SuiteName = "Suite", XmlContent = "<xml/>", Format = "junit" }, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(10, ((TestIngestionResultDto)ok.Value!).TotalCases);
    }

    // Negative-authorization coverage for the two endpoints that enforce project ownership in-controller
    // (Create/Update/Delete are [Authorize(Roles=Admin)] - enforced by the framework, not unit-testable here).

    [Fact]
    public async Task GetSuites_ProjectNotAccessible_Forbids()
    {
        // A non-admin whose accessible project set excludes the requested projectId must be refused.
        _authzMock.GetAccessibleResourceIdsAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, Permission.Read, Arg.Any<CancellationToken>())
            .Returns([2]);

        var result = await _sut.GetSuites(99, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().GetSuitesAsync(Arg.Any<int?>(), Arg.Any<List<int>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuite_WithoutProjectReadPermission_Forbids()
    {
        _serviceMock.GetSuiteDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TestSuiteDetailDto { Id = 1, Name = "Suite1", ProjectId = 7 });
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 7, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetSuite(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }
}

