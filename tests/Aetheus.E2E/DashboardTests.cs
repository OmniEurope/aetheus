// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Dashboard")]
public class DashboardTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
        await NavigateToAsync("");
    }

    [Test]
    public async Task Dashboard_RendersExpectedContentAndNavigation()
    {
        await Expect(Page.GetByText("Dashboard").First).ToBeVisibleAsync(new() { Timeout = 5000 });
        await WaitForNoSpinnerAsync();

        var cards = Page.Locator(".rz-card");
        await Expect(cards.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var cardCount = await cards.CountAsync();
        Assert.That(cardCount, Is.GreaterThanOrEqualTo(4), "Dashboard should show at least 4 stat cards");

        await Expect(Page.GetByText("Total Servers").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page.GetByText("Online").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Offline").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Pending Tasks").First).ToBeVisibleAsync();

        var serversCard = Page.Locator(".rz-card").Filter(new() { HasText = "Servers" });
        await Expect(serversCard.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        var runsCard = Page.Locator(".rz-card").Filter(new() { HasText = "Recent Pipeline Runs" });
        await Expect(runsCard.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Dashboard"));

        var sidebar = Page.Locator(".rz-sidebar");
        await Expect(sidebar).ToBeVisibleAsync();
        var nav = Page.Locator(".rz-panel-menu");
        await Expect(nav).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Only the top-level RadzenPanelMenuItems are always rendered. Pipelines/Tasks/Logs
        // are contextual sub-items that only appear once you drill into the Servers/Projects
        // section, so they are not present on the dashboard. Radzen 10 renders the item
        // label as `.rz-navigation-item-text`; match it exactly to avoid the strict-mode
        // clash with the wrapping anchor element.
        await Expect(SidebarNavItem("Dashboard").First).ToBeVisibleAsync();
        await Expect(SidebarNavItem("Servers").First).ToBeVisibleAsync();
        await Expect(SidebarNavItem("Projects").First).ToBeVisibleAsync();
        await Expect(SidebarNavItem("Settings").First).ToBeVisibleAsync();

        // "Aetheus" can appear twice in the header (app title + the default
        // organization name in the org picker), so target the app title element.
        var appTitle = Page.Locator(".rz-header .app-title");
        await Expect(appTitle).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(appTitle).ToContainTextAsync("Aetheus");

        // The theme toggle is a switch inside the header user dropdown menu,
        // not a standalone header button.
        await Page.Locator(".header-user-btn").ClickAsync();
        var themeSwitch = Page.Locator(".theme-switch-readonly");
        await Expect(themeSwitch).ToBeVisibleAsync(new() { Timeout = 5000 });
    }
}
