// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using AlertsPage = Aetheus.Front.Components.Alerts.Alerts;

namespace Aetheus.Front.Tests.Pages.AlertsExtended;

/// <summary>
/// Extended coverage for Alerts.razor.cs - the GetMetricBadgeStyle / GetSeverityBadgeStyle
/// static badge mappers. CreateAlert / DeleteAlert behaviour lives in AlertEditDialogTests
/// (create POST + close) - they route through Dialog.Confirm which cannot be driven here.
/// </summary>
public class AlertsExtendedTests : BunitContext
{
    private static readonly BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    // CreateAlert_WithName_ReturnsWithoutError removed: it never invoked CreateAlert - it only
    // asserted the init GET and the "CPU High" render (a misnamed smoke test). The real create
    // POST is covered by AlertEditDialogTests.Submit_CreateMode_WithName_SendsCreatePost_AndSucceeds.

    // DeleteAlert removed - calls Dialog.Confirm which hangs in bUnit

    // ── GetMetricBadgeStyle - all enum values ─────────────────────────────────

    [Theory]
    [InlineData(MetricType.Cpu, OmniTone.Warning)]
    [InlineData(MetricType.Memory, OmniTone.Accent)]
    [InlineData(MetricType.Disk, OmniTone.Danger)]
    public void GetMetricBadgeStyle_ReturnsExpected(MetricType metric, OmniTone expected)
    {
        var method = typeof(AlertsPage).GetMethod("GetMetricBadgeStyle", Stat)!;
        var result = (OmniTone)method.Invoke(null, [metric])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetMetricBadgeStyle_Default_ReturnsLight()
    {
        var method = typeof(AlertsPage).GetMethod("GetMetricBadgeStyle", Stat)!;
        var result = (OmniTone)method.Invoke(null, [(MetricType)999])!;
        Assert.Equal(OmniTone.Neutral, result);
    }

    // ── GetSeverityBadgeStyle - all enum values ────────────────────────────────

    [Theory]
    [InlineData(AlertSeverity.Critical, OmniTone.Danger)]
    [InlineData(AlertSeverity.Warning, OmniTone.Warning)]
    [InlineData(AlertSeverity.Info, OmniTone.Accent)]
    public void GetSeverityBadgeStyle_ReturnsExpected(AlertSeverity severity, OmniTone expected)
    {
        var method = typeof(AlertsPage).GetMethod("GetSeverityBadgeStyle", Stat)!;
        var result = (OmniTone)method.Invoke(null, [severity])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSeverityBadgeStyle_Default_ReturnsLight()
    {
        var method = typeof(AlertsPage).GetMethod("GetSeverityBadgeStyle", Stat)!;
        var result = (OmniTone)method.Invoke(null, [(AlertSeverity)999])!;
        Assert.Equal(OmniTone.Neutral, result);
    }
}
