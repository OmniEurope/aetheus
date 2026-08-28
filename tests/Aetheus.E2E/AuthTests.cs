// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Auth")]
public class AuthTests : E2ETestBase
{
    protected override bool UsePreauthenticatedContext => false;

    [Test]
    public async Task Login_ValidCredentials_RedirectsToDashboard()
    {
        await Page.GotoAsync($"{FrontendUrl}/login");
        await Page.WaitForSelectorAsync("input[name='Username']", new() { Timeout = 15000 });

        await Page.FillAsync("input[name='Username']", AdminUser);
        await Page.FillAsync("input[name='Password']", AdminPassword);
        await Page.ClickAsync("button[type='submit']");

        await Page.WaitForURLAsync($"{FrontendUrl}/", new() { Timeout = 10000 });
        await Expect(Page).ToHaveURLAsync($"{FrontendUrl}/");
    }

    [Test]
    public async Task Login_InvalidCredentials_ShowsError()
    {
        // This test deliberately submits invalid credentials; the browser correctly reports the
        // API's 401 as a failed resource. Keep that one expected diagnostic scoped to this test.
        AllowBrowserDiagnostic(new System.Text.RegularExpressions.Regex(
            @"status of 401",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await Page.GotoAsync($"{FrontendUrl}/login");
        await Page.WaitForSelectorAsync("input[name='Username']", new() { Timeout = 15000 });

        await Page.FillAsync("input[name='Username']", "wronguser");
        await Page.FillAsync("input[name='Password']", "wrongpassword");
        await Page.ClickAsync("button[type='submit']");

        var alert = Page.Locator(".rz-alert");
        await Expect(alert).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Login_EmptyFields_ShowsValidation()
    {
        await Page.GotoAsync($"{FrontendUrl}/login");
        await Page.WaitForSelectorAsync("input[name='Username']", new() { Timeout = 15000 });

        await Page.FillAsync("input[name='Username']", "");
        await Page.FillAsync("input[name='Password']", "");
        await Page.ClickAsync("button[type='submit']");

        // The login form uses Radzen validators (RadzenRequiredValidator), which
        // render their messages as `.rz-message`, not Blazor's `.validation-message`.
        var validationMessages = Page.Locator(".rz-message");
        await Expect(validationMessages.First).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task UnauthenticatedUser_RedirectsToLogin()
    {
        await Page.GotoAsync($"{FrontendUrl}/servers");

        // Wait on the redirect itself, not LoadState.NetworkIdle: the WASM app keeps a SignalR WebSocket
        // open so the network is rarely idle (E2ETestBase documents this as the fragile pattern). The auth
        // guard bounces an unauthenticated user to /login - assert directly on that URL transition.
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/login"), new() { Timeout = 15000 });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task Logout_RedirectsToLogin()
    {
        await LoginAsync();

        // Logout lives inside the header user dropdown menu, which must be opened first.
        await Page.Locator(".header-user-btn").ClickAsync();
        // The clickable menu row carries `.user-menu-section-clickable`; filtering by
        // text avoids the strict-mode clash with the inner RadzenText element.
        var logoutRow = Page.Locator(".user-menu-section-clickable")
            .Filter(new() { HasText = "Logout" });
        await Expect(logoutRow).ToBeVisibleAsync(new() { Timeout = 5000 });
        await logoutRow.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"), new() { Timeout = 5000 });
    }

    [Test]
    public async Task Login_PageLoadsWithExpectedTitleAndForm()
    {
        var response = await Page.GotoAsync($"{FrontendUrl}/login");
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Ok, Is.True);
        await Page.WaitForSelectorAsync("input[name='Username']", new() { Timeout = 15000 });

        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Login"));
        await Expect(Page.Locator("input[name='Username']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name='Password']")).ToBeVisibleAsync();
        await Expect(Page.Locator("button[type='submit']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_MobileCard_RemainsInsideViewport()
    {
        await Page.SetViewportSizeAsync(375, 667);
        await Page.GotoAsync($"{FrontendUrl}/login");
        await Page.WaitForSelectorAsync(".login-container .rz-card", new() { Timeout = 15000 });

        var box = await Page.Locator(".login-container .rz-card").BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(box!.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(375));
        });
        var hasHorizontalOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.That(hasHorizontalOverflow, Is.False);
    }
}
