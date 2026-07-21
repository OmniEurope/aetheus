// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Alerts;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AlertsControllerTests
{
    private readonly IAlertService _serviceMock = Substitute.For<IAlertService>();
    private readonly AlertsController _sut;

    public AlertsControllerTests()
    {
        _sut = new AlertsController(_serviceMock);
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
    public async Task GetAlertRules_ReturnsOkWithList()
    {
        _serviceMock.GetAlertRulesAsync(Arg.Any<CancellationToken>())
            .Returns([new AlertRuleDto { Id = 1, Name = "Rule1" }]);

        var result = await _sut.GetAlertRules(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<AlertRuleDto>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetAlertRule_Found_ReturnsOk()
    {
        _serviceMock.GetAlertRuleAsync(1, Arg.Any<CancellationToken>())
            .Returns(new AlertRuleDto { Id = 1, Name = "Rule1" });

        var result = await _sut.GetAlertRule(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Rule1", ((AlertRuleDto)ok.Value!).Name);
    }

    [Fact]
    public async Task GetAlertRule_NotFound_ReturnsNotFound()
    {
        _serviceMock.GetAlertRuleAsync(999, Arg.Any<CancellationToken>())
            .Returns((AlertRuleDto?)null);

        var result = await _sut.GetAlertRule(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateAlertRule_ReturnsCreatedAtAction()
    {
        _serviceMock.CreateAlertRuleAsync(Arg.Any<CreateAlertRuleRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AlertRuleDto { Id = 1, Name = "New" });

        var result = await _sut.CreateAlertRule(new CreateAlertRuleRequest { Name = "New", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 90 }, TestContext.Current.CancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(1, ((AlertRuleDto)created.Value!).Id);
    }

    [Fact]
    public async Task UpdateAlertRule_Found_ReturnsOk()
    {
        _serviceMock.UpdateAlertRuleAsync(1, Arg.Any<UpdateAlertRuleRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AlertRuleDto { Id = 1, Name = "Updated" });

        var result = await _sut.UpdateAlertRule(1, new UpdateAlertRuleRequest { Name = "Updated", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 90 }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateAlertRule_NotFound_ReturnsNotFound()
    {
        _serviceMock.UpdateAlertRuleAsync(999, Arg.Any<UpdateAlertRuleRequest>(), Arg.Any<CancellationToken>())
            .Returns((AlertRuleDto?)null);

        var result = await _sut.UpdateAlertRule(999, new UpdateAlertRuleRequest { Name = "X", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteAlertRule_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteAlertRuleAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteAlertRule(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteAlertRule_NotFound_ReturnsNotFound()
    {
        _serviceMock.DeleteAlertRuleAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteAlertRule(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
