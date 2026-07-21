// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Monitoring;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class MonitoringControllerTests
{
    private readonly IMonitoringService _serviceMock = Substitute.For<IMonitoringService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly MonitoringController _sut;

    public MonitoringControllerTests()
    {
        _authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new MonitoringController(_serviceMock, _authz);
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
    public async Task GetDashboard_ReturnsOkWithDashboard()
    {
        var dashboard = new DashboardOverviewDto { TotalServers = 3, OnlineServers = 2 };
        _serviceMock.GetDashboardAsync(Arg.Any<List<int>?>(), Arg.Any<List<int>?>(), Arg.Any<List<int>?>(), Arg.Any<CancellationToken>()).Returns(dashboard);

        var result = await _sut.GetDashboard(TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DashboardOverviewDto>(okResult.Value);
        Assert.Equal(3, dto.TotalServers);
        Assert.Equal(2, dto.OnlineServers);
    }

    [Fact]
    public async Task GetDashboard_UserWithoutRights_PassesEmptyFiltersToService()
    {
        _authz.GetAccessibleResourceIdsAsync(
                Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Permission.Read, Arg.Any<CancellationToken>())
            .Returns([]);
        _serviceMock.GetDashboardAsync(
                Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
                Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
                Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
                Arg.Any<CancellationToken>())
            .Returns(new DashboardOverviewDto());

        var result = await _sut.GetDashboard(TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
        await _serviceMock.Received(1).GetDashboardAsync(
            Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
            Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
            Arg.Is<List<int>?>(ids => ids != null && ids.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetServerMetrics_ReturnsOkWithMetrics()
    {
        var metrics = new List<ServerMetricDto>
        {
            new() { CpuPercent = 55.0, Timestamp = DateTime.UtcNow }
        };
        _serviceMock.GetServerMetricsAsync(1, 24, Arg.Any<CancellationToken>()).Returns(metrics);

        var result = await _sut.GetServerMetrics(1, 24, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<List<ServerMetricDto>>(okResult.Value);
        Assert.Single(data);
        Assert.Equal(55.0, data[0].CpuPercent);
    }

    [Fact]
    public async Task GetServerMetrics_DefaultHours_Uses24()
    {
        _serviceMock.GetServerMetricsAsync(1, 24, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.GetServerMetrics(1, ct: TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
        await _serviceMock.Received(1).GetServerMetricsAsync(1, 24, Arg.Any<CancellationToken>());
    }
}
