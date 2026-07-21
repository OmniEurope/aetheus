// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Alerts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AlertServiceFullTests
{
    private readonly IAlertRepository _repoMock = Substitute.For<IAlertRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly IHubContext<AlertHub> _hubMock = Substitute.For<IHubContext<AlertHub>>();
    private readonly IClientProxy _alertGroup = Substitute.For<IClientProxy>();
    private readonly AlertService _sut;

    public AlertServiceFullTests()
    {
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(_alertGroup);
        _hubMock.Clients.Returns(clients);
        _sut = new AlertService(_repoMock, _auditMock, _hubMock, TimeProvider.System);
    }

    private Task AssertRuleChangedBroadcast(int times) =>
        _alertGroup.Received(times).SendCoreAsync("AlertRuleChanged", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task GetAlertRulesAsync_ReturnsMappedList()
    {
        _repoMock.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new AlertRule
            {
                Id = 1, Name = "CPU High", ServerId = 10, Metric = MetricType.Cpu,
                Operator = ComparisonOperator.GreaterThan, Threshold = 90,
                Severity = AlertSeverity.Critical, IsEnabled = true,
                Server = new Server { Name = "srv1" }
            }]);

        var result = await _sut.GetAlertRulesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("CPU High", result[0].Name);
        Assert.Equal("srv1", result[0].ServerName);
        Assert.Equal(AlertSeverity.Critical, result[0].Severity);
    }

    [Fact]
    public async Task GetAlertRuleAsync_Found_ReturnsDto()
    {
        _repoMock.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new AlertRule { Id = 1, Name = "Test", Metric = MetricType.Memory, Operator = ComparisonOperator.GreaterThan, Threshold = 80 });

        var result = await _sut.GetAlertRuleAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task GetAlertRuleAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((AlertRule?)null);

        var result = await _sut.GetAlertRuleAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAlertRuleAsync_CreatesAndReloadsAndAudits()
    {
        var request = new CreateAlertRuleRequest
        {
            Name = "New Rule",
            ServerId = 10,
            Metric = MetricType.Disk,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 95,
            SustainedSeconds = 60,
            Severity = AlertSeverity.Warning
        };

        _repoMock.AddAsync(Arg.Any<AlertRule>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AlertRule { Id = 1, Name = "New Rule", Metric = MetricType.Disk, Operator = ComparisonOperator.GreaterThan, Threshold = 95, Severity = AlertSeverity.Warning });

        var result = await _sut.CreateAlertRuleAsync(request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("New Rule", result.Name);
        Assert.Equal(AlertSeverity.Warning, result.Severity);
        await _repoMock.Received(1).AddAsync(Arg.Is<AlertRule>(a => a.Name == "New Rule"), Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Created", "AlertRule", Arg.Any<int>(), "New Rule", Arg.Any<CancellationToken>());
        await AssertRuleChangedBroadcast(1);
    }

    [Fact]
    public async Task UpdateAlertRuleAsync_Found_UpdatesAndAudits()
    {
        var entity = new AlertRule { Id = 1, Name = "Old", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 50 };
        _repoMock.FindAsync(1, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var request = new UpdateAlertRuleRequest
        {
            Name = "Updated",
            ServerId = 20,
            Metric = MetricType.Memory,
            Operator = ComparisonOperator.LessThan,
            Threshold = 10,
            SustainedSeconds = 120,
            Severity = AlertSeverity.Info,
            IsEnabled = false
        };

        var result = await _sut.UpdateAlertRuleAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
        Assert.False(result.IsEnabled);
        await _auditMock.Received(1).LogAsync("Updated", "AlertRule", 1, "Updated", Arg.Any<CancellationToken>());
        await AssertRuleChangedBroadcast(1);
    }

    [Fact]
    public async Task UpdateAlertRuleAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindAsync(999, Arg.Any<CancellationToken>()).Returns((AlertRule?)null);

        var result = await _sut.UpdateAlertRuleAsync(999, new UpdateAlertRuleRequest { Name = "X" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await AssertRuleChangedBroadcast(0);
    }

    [Fact]
    public async Task DeleteAlertRuleAsync_Found_DeletesAndAudits()
    {
        var entity = new AlertRule { Id = 1, Name = "ToDelete", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 90 };
        _repoMock.FindAsync(1, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.DeleteAlertRuleAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).RemoveAsync(entity, Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Deleted", "AlertRule", 1, "ToDelete", Arg.Any<CancellationToken>());
        await AssertRuleChangedBroadcast(1);
    }

    [Fact]
    public async Task DeleteAlertRuleAsync_NotFound_ReturnsFalse()
    {
        _repoMock.FindAsync(999, Arg.Any<CancellationToken>()).Returns((AlertRule?)null);

        var result = await _sut.DeleteAlertRuleAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task GetAlertRulesAsync_EmptyList_ReturnsEmpty()
    {
        _repoMock.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.GetAlertRulesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}
