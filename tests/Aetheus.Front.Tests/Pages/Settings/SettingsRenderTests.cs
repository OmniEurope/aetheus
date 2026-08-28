// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Settings;
using Aetheus.Shared.DTOs;
using Bunit;
using SettingsPage = Aetheus.Front.Pages.Settings.Settings;

namespace Aetheus.Front.Tests.Pages;

public class SettingsRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public SettingsRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: false);
    }

    private void SetupStubs()
    {
        _handler.SetJsonResponse(HttpMethod.Get, "api/users/me", new UserDto
        {
            Id = 1,
            Username = "testuser",
            IsActive = true,
            TotpEnabled = false,
            Roles = ["User"]
        });
    }

    [Fact]
    public void Renders_BasicMarkup()
    {
        SetupStubs();

        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // OnInitializedAsync loads the current user (GetCurrentUserAsync → api/users/me) and
        // renders the localized page heading.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/users/me"));
        Assert.Contains("Settings", cut.Markup);
    }

    [Fact]
    public void Renders_WithProfileTab()
    {
        SetupStubs();

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "profile"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var index = (int)typeof(SettingsPage)
            .GetField("_selectedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(0, index);
    }

    [Fact]
    public void Renders_WithAppearanceTab()
    {
        SetupStubs();

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "appearance"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var index = (int)typeof(SettingsPage)
            .GetField("_selectedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(1, index);
    }

    [Fact]
    public void Renders_WithSecurityTab()
    {
        SetupStubs();

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "security"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // "security" is index 3 in TabNames.
        var index = (int)typeof(SettingsPage)
            .GetField("_selectedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(3, index);
    }

    [Fact]
    public void Renders_WithAboutTab()
    {
        SetupStubs();

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "about"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // "about" is index 5 (last) in TabNames, after the tokens tab.
        var index = (int)typeof(SettingsPage)
            .GetField("_selectedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(5, index);
    }

    [Fact]
    public async Task SaveProfile_CallsToast()
    {
        SetupStubs();

        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var method = typeof(SettingsPage).GetMethod("SaveProfile", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // SaveProfile persists the display name to localStorage (no server profile endpoint).
        var write = JSInterop.Invocations.Single(i =>
            i.Identifier == "localStorage.setItem" && (string?)i.Arguments[0] == "aetheus_display_name");
        Assert.Equal("aetheus_display_name", write.Arguments[0]);
    }

    [Fact]
    public void NotificationToggle_PersistsImmediately()
    {
        SetupStubs();

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "notifications"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        cut.Find("input.labeled-toggle-native-input").Change(false);

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "localStorage.setItem"
            && (string?)invocation.Arguments[0] == "aetheus_notif_email"
            && (string?)invocation.Arguments[1] == "false"));
    }

    [Fact]
    public void OnTabChange_UpdatesSelectedIndex()
    {
        SetupStubs();

        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var method = typeof(SettingsPage).GetMethod("OnTabChange", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cut.InvokeAsync(() => method.Invoke(cut.Instance, [2]));

        var index = (int)typeof(SettingsPage)
            .GetField("_selectedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(2, index);
    }

    [Fact]
    public void Renders_WithTotpEnabled()
    {
        // GetCurrentUserAsync fetches api/users/me (not api/auth/me)
        _handler.SetJsonResponse("api/users/me", new UserDto
        {
            Id = 1,
            Username = "testuser",
            IsActive = true,
            TotpEnabled = true,
            Roles = ["User"]
        });

        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "security"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var totpEnabled = (bool)typeof(SettingsPage)
            .GetField("_totpEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.True(totpEnabled);
    }

    [Fact]
    public async Task SetupTotp_PopulatesSetupResponse()
    {
        SetupStubs();
        _handler.SetJsonResponse("api/auth/totp/setup", new TotpSetupResponse
        {
            SharedKey = "JBSWY3DPEHPK3PXP",
            AuthenticatorUri = "otpauth://totp/test",
            RecoveryCodes = ["code1", "code2"]
        });

        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var method = typeof(SettingsPage).GetMethod("SetupTotp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var setup = (TotpSetupResponse?)typeof(SettingsPage)
            .GetField("_totpSetup", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.NotNull(setup);
        Assert.Equal("JBSWY3DPEHPK3PXP", setup!.SharedKey);
    }
}
