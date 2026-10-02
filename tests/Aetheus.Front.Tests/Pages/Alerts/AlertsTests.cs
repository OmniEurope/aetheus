// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class AlertsTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AlertsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyGrid_WhenNoAlerts()
    {
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>());
        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("NoRecords") || !cut.Markup.Contains("rz-progressbar"));
        Assert.DoesNotContain("rz-progressbar-circular", cut.Markup);
    }

    [Fact]
    public void Renders_AlertRows()
    {
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>
        {
            new() { Id = 1, Name = "High CPU", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 90, SustainedSeconds = 60, IsEnabled = true, CreatedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Low Disk", Metric = MetricType.Disk, Operator = ComparisonOperator.GreaterThanOrEqual, Threshold = 85, SustainedSeconds = 120, IsEnabled = false, CreatedAt = DateTime.UtcNow }
        });

        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("High CPU"));

        Assert.Contains("High CPU", cut.Markup);
        Assert.Contains("Low Disk", cut.Markup);
        Assert.Contains("Active", cut.Markup);
        Assert.Contains("Disabled", cut.Markup);
    }

    [Fact]
    public void Renders_LastTriggered_Badge()
    {
        var triggeredAt = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>
        {
            new() { Id = 1, Name = "Mem Alert", Metric = MetricType.Memory, Operator = ComparisonOperator.GreaterThan, Threshold = 80, SustainedSeconds = 60, IsEnabled = true, LastTriggeredAt = triggeredAt, CreatedAt = DateTime.UtcNow }
        });

        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("Mem Alert"));

        Assert.Contains("Mem Alert", cut.Markup);
    }

    [Fact]
    public void CreateButton_Rendered()
    {
        // X4D8: the Create button now opens AlertEditDialog (the form is no longer inline).
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>());
        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("Create"));

        var createButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Create"));
        Assert.NotNull(createButton);
    }

    [Fact]
    public void Renders_SeverityBadge_WhenPresent()
    {
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>
        {
            new() { Id = 1, Name = "Critical Alert", Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 95, SustainedSeconds = 30, IsEnabled = true, Severity = AlertSeverity.Critical, CreatedAt = DateTime.UtcNow }
        });

        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("Critical Alert"));

        Assert.Contains("Critical", cut.Markup);
    }

    [Fact]
    public void Handles_HttpError_Gracefully()
    {
        _handler.SetResponse("api/alerts", System.Net.HttpStatusCode.InternalServerError);
        var cut = Render<Alerts>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar"));
        Assert.DoesNotContain("rz-progressbar-circular", cut.Markup);
    }

    [Theory]
    [InlineData(MetricType.Cpu, OmniTone.Warning)]
    [InlineData(MetricType.Memory, OmniTone.Accent)]
    [InlineData(MetricType.Disk, OmniTone.Danger)]
    public void GetMetricBadgeStyle_ReturnsExpected(MetricType metric, OmniTone expected)
    {
        var method = typeof(Alerts).GetMethod("GetMetricBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (OmniTone)method.Invoke(null, [metric])!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(AlertSeverity.Critical, OmniTone.Danger)]
    [InlineData(AlertSeverity.Warning, OmniTone.Warning)]
    [InlineData(AlertSeverity.Info, OmniTone.Accent)]
    public void GetSeverityBadgeStyle_ReturnsExpected(AlertSeverity severity, OmniTone expected)
    {
        var method = typeof(Alerts).GetMethod("GetSeverityBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (OmniTone)method.Invoke(null, [severity])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Renders_MultipleMetricTypes()
    {
        _handler.SetJsonResponse("api/alerts", new List<AlertRuleDto>
        {
            new() { Id = 1, Name = "CPU Alert", Metric = MetricType.Cpu, IsEnabled = true, Threshold = 80, SustainedSeconds = 30, CreatedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Mem Alert", Metric = MetricType.Memory, IsEnabled = true, Threshold = 70, SustainedSeconds = 60, CreatedAt = DateTime.UtcNow },
            new() { Id = 3, Name = "Disk Alert", Metric = MetricType.Disk, IsEnabled = true, Threshold = 90, SustainedSeconds = 120, CreatedAt = DateTime.UtcNow }
        });

        var cut = Render<Alerts>();
        cut.WaitForState(() => cut.Markup.Contains("CPU Alert"));

        Assert.Contains("CPU Alert", cut.Markup);
        Assert.Contains("Mem Alert", cut.Markup);
        Assert.Contains("Disk Alert", cut.Markup);
    }
}
