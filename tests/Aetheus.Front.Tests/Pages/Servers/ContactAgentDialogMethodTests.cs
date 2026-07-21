// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ContactAgentDialogMethodTests
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type DialogType = typeof(ContactAgentDialog);

    // === TokenBarClass (private static) ===

    [Theory]
    [InlineData(-5.0, "token-bar-expired")]
    [InlineData(0.0, "token-bar-critical")]  // 0 is not < 0, falls to < 7 branch
    [InlineData(3.0, "token-bar-critical")]
    [InlineData(6.9, "token-bar-critical")]
    [InlineData(7.0, "token-bar-warning")]
    [InlineData(29.0, "token-bar-warning")]
    [InlineData(30.0, "token-bar-ok")]
    [InlineData(365.0, "token-bar-ok")]
    public void TokenBarClass_ReturnsExpectedClass(double daysRemaining, string expected)
    {
        var method = DialogType.GetMethod("TokenBarClass", PrivStatic)!;
        var result = (string)method.Invoke(null, [daysRemaining])!;
        Assert.Equal(expected, result);
    }

    // === TokenBarPercent (private static) ===

    [Theory]
    [InlineData(-10.0, 0)]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 2)]   // 1/365*100 ≈ 0.27 → clamped to 2
    [InlineData(365.0, 100)]
    [InlineData(730.0, 100)] // clamped to 100
    [InlineData(182.5, 50)]
    public void TokenBarPercent_ReturnsExpectedPercent(double daysRemaining, int expected)
    {
        var method = DialogType.GetMethod("TokenBarPercent", PrivStatic)!;
        var result = (int)method.Invoke(null, [daysRemaining])!;
        Assert.Equal(expected, result);
    }

    // === ResolveError (private instance) ===

    [Fact]
    public void ResolveError_NullResult_ReturnsContactAgentRequestFailedKey()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("ResolveError", Priv)!;
        var result = (string)method.Invoke(instance, [null])!;
        Assert.Contains("ContactAgentRequestFailed", result);
    }

    [Fact]
    public void ResolveError_EmptyError_ReturnsUnreachableKey()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("ResolveError", Priv)!;
        var dto = new ContactAgentResultDto { Reachable = false, Error = "" };
        var result = (string)method.Invoke(instance, [dto])!;
        Assert.Contains("ContactAgentUnreachable", result);
    }

    [Fact]
    public void ResolveError_WhitespaceError_ReturnsUnreachableKey()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("ResolveError", Priv)!;
        var dto = new ContactAgentResultDto { Reachable = false, Error = "   " };
        var result = (string)method.Invoke(instance, [dto])!;
        Assert.Contains("ContactAgentUnreachable", result);
    }

    [Fact]
    public void ResolveError_WithError_ReturnsErrorMessage()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("ResolveError", Priv)!;
        var dto = new ContactAgentResultDto { Reachable = false, Error = "Connection refused" };
        var result = (string)method.Invoke(instance, [dto])!;
        Assert.Equal("Connection refused", result);
    }

    // === FormatSeconds (private instance) ===

    [Fact]
    public void FormatSeconds_LessThan60_ReturnsSecondsFormat()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("FormatSeconds", Priv)!;
        var result = (string)method.Invoke(instance, [45.0])!;
        Assert.Contains("ContactAgentSecondsAgo", result);
    }

    [Fact]
    public void FormatSeconds_Minutes_ReturnsMinutesFormat()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("FormatSeconds", Priv)!;
        var result = (string)method.Invoke(instance, [300.0])!;
        Assert.Contains("ContactAgentMinutesAgo", result);
    }

    [Fact]
    public void FormatSeconds_Hours_ReturnsHoursFormat()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("FormatSeconds", Priv)!;
        var result = (string)method.Invoke(instance, [7200.0])!;
        Assert.Contains("ContactAgentHoursAgo", result);
    }

    // === DisposeAsync (no hub) ===

    [Fact]
    public async Task DisposeAsync_NullHub_Completes()
    {
        var instance = CreateInstance();
        await instance.DisposeAsync();
    }

    // === Helpers ===

    private static ContactAgentDialog CreateInstance()
    {
        var instance = new ContactAgentDialog();
        var localizer = new BunitTestHelper.StubLocalizer();
        DialogType.GetProperty("L", Priv)!.SetValue(instance, localizer);
        return instance;
    }
}
