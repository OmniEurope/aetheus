// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Alerts")]
public class AlertsTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Alerts_PageRendersExpectedControls()
    {
        await NavigateToAsync("alerts");
        await Expect(Page.GetByText("Alerts").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Alerts"));
        await WaitForNoSpinnerAsync();
        await WaitForDataGridAsync();

        var grid = Page.Locator(".rz-data-grid, .rz-datatable").First;
        await Expect(grid).ToBeVisibleAsync(new() { Timeout = 10000 });
        var createButton = Page.Locator("button:has-text('Create')").First;
        await Expect(createButton).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task Alerts_NewAlert_CreatesRule()
    {
        await NavigateToAsync("alerts");
        await Page.Locator("button:has-text('Create')").First.ClickAsync();

        var dialog = Page.Locator(".rz-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10000 });
        var ruleName = $"E2E alert {DateTime.UtcNow:yyyyMMddHHmmssfff}";
        await dialog.Locator("input").First.FillAsync(ruleName);
        await dialog.Locator("button:has-text('Create')").ClickAsync();

        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10000 });
        await Expect(Page.GetByText(ruleName, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task Alerts_NavigateFromSidebar_Works()
    {
        // "Alerts" is a contextual sub-item of the Servers section, only rendered while inside that
        // section (NavMenu renders it under @if (IsOnSection("servers"))). Start from /servers so it
        // is in the sidebar - same approach as the Tasks/Logs sidebar tests (its sibling items).
        await NavigateToAsync("servers");
        await ClickSidebarNavItemAsync("Alerts");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/alerts"), new() { Timeout = 5000 });
    }
}
