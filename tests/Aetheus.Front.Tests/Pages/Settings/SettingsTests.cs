// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Bunit;
using SettingsPage = Aetheus.Front.Pages.Settings.Settings;

namespace Aetheus.Front.Tests.Pages;

public class SettingsTests : BunitContext
{
    public SettingsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: false);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/users/me",
            new Aetheus.Shared.DTOs.UserDto
            {
                Id = 1,
                Username = "testuser",
                IsActive = true,
                Roles = ["User"]
            });
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_Settings_Page()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        Assert.Contains("Settings", cut.Markup);
    }

    [Fact]
    public void Renders_ForNonAdmin()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // Settings is not admin-gated - a non-admin user still gets the full settings page.
        Assert.Contains("Settings", cut.Markup);
    }

    [Fact]
    public async Task SaveProfile_ShowsToast()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(() => cut.Instance.GetType()
            .GetMethod("SaveProfile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cut.Instance, null));

        // SaveProfile persists the display name to localStorage before toasting success.
        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem" && (string?)i.Arguments[0] == "aetheus_display_name");
    }

    [Fact]
    public async Task SaveAppearance_WritesToLocalStorage()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(() => cut.Instance.GetType()
            .GetMethod("SaveAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cut.Instance, null));

        // SaveAppearance persists both the theme and language selections to localStorage.
        var keys = JSInterop.Invocations
            .Where(i => i.Identifier == "localStorage.setItem")
            .Select(i => (string?)i.Arguments[0])
            .ToList();
        Assert.Contains("aetheus_theme", keys);
        Assert.Contains("aetheus_lang", keys);
    }
}
