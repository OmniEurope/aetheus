// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Dashboards;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class DashboardsControllerTests
{
    private readonly IDashboardService _serviceMock = Substitute.For<IDashboardService>();
    private readonly DashboardsController _sut;

    public DashboardsControllerTests()
    {
        _sut = new DashboardsController(_serviceMock);
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
    public async Task GetDashboards_ReturnsOk()
    {
        _serviceMock.GetUserDashboardsAsync(1, TestContext.Current.CancellationToken)
            .Returns([new DashboardDto { Id = 1, Name = "Main" }]);

        var result = await _sut.GetDashboards(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<DashboardDto>)ok.Value!);
    }

    [Fact]
    public async Task GetDashboard_NotFound_Returns404()
    {
        _serviceMock.GetDashboardAsync(99, Arg.Any<int>(), TestContext.Current.CancellationToken).Returns((DashboardDto?)null);

        var result = await _sut.GetDashboard(99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetDashboard_Found_ReturnsOk()
    {
        _serviceMock.GetDashboardAsync(1, Arg.Any<int>(), TestContext.Current.CancellationToken)
            .Returns(new DashboardDto { Id = 1, Name = "Ops" });

        var result = await _sut.GetDashboard(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Ops", ((DashboardDto)ok.Value!).Name);
    }

    [Fact]
    public async Task CreateDashboard_ReturnsCreated()
    {
        var request = new CreateDashboardRequest { Name = "New", Widgets = [] };
        _serviceMock.CreateDashboardAsync(1, request, TestContext.Current.CancellationToken)
            .Returns(new DashboardDto { Id = 5, Name = "New" });

        var result = await _sut.CreateDashboard(request, TestContext.Current.CancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(5, ((DashboardDto)created.Value!).Id);
    }

    [Fact]
    public async Task UpdateDashboard_NotFound_Returns404()
    {
        _serviceMock.UpdateDashboardAsync(99, 1, Arg.Any<UpdateDashboardRequest>(), TestContext.Current.CancellationToken)
            .Returns((DashboardDto?)null);

        var result = await _sut.UpdateDashboard(99, new UpdateDashboardRequest { Name = "X", Widgets = [] }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteDashboard_NotFound_Returns404()
    {
        _serviceMock.DeleteDashboardAsync(99, 1, TestContext.Current.CancellationToken).Returns(false);

        var result = await _sut.DeleteDashboard(99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteDashboard_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteDashboardAsync(1, 1, TestContext.Current.CancellationToken).Returns(true);

        var result = await _sut.DeleteDashboard(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }
}
