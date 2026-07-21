// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Servers")]
public class ServersTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Servers_PageRendersExpectedControlsAndColumns()
    {
        await NavigateToAsync("servers");
        await Expect(Page.GetByText("Servers").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Servers"));
        await WaitForNoSpinnerAsync();
        await WaitForDataGridAsync();

        var grid = Page.Locator(".server-list-grid");
        await Expect(grid).ToBeVisibleAsync(new() { Timeout = 10000 });
        var searchBox = Page.Locator(".server-list-search");
        await Expect(searchBox).ToBeVisibleAsync(new() { Timeout = 10000 });
        var typeDropdown = Page.Locator(".rz-dropdown").First;
        await Expect(typeDropdown).ToBeVisibleAsync(new() { Timeout = 10000 });
        var refreshButton = Page.Locator("button[title='Refresh']");
        await Expect(refreshButton).ToBeVisibleAsync();

        // Radzen 10 RadzenDataGrid renders one `.rz-column-title` per column header.
        var headers = Page.Locator(".rz-column-title");
        await Expect(headers.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var count = await headers.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(5), "Servers grid should have at least 5 columns");

        var badge = Page.Locator(".rz-badge").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"\d+\s*total", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await Expect(badge.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        var searchResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("/api/servers?", StringComparison.OrdinalIgnoreCase)
            && response.Url.Contains("search=web-01", StringComparison.OrdinalIgnoreCase));
        await searchBox.FillAsync("web-01");
        await searchBox.DispatchEventAsync("change");
        var filteredResponse = await searchResponse.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(filteredResponse.Ok, Is.True, $"Server search returned HTTP {filteredResponse.Status}.");
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "web-01" }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "build-01" }))
            .ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear filters" }).ClickAsync();
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "build-01" }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        await typeDropdown.ClickAsync();
        await Page.Locator(".rz-dropdown-panel:visible .rz-dropdown-item")
            .Filter(new() { HasText = "Build" }).ClickAsync();
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "build-01" }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "web-01" }))
            .ToHaveCountAsync(0);

        var manualRefresh = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("/api/servers?", StringComparison.OrdinalIgnoreCase));
        await refreshButton.ClickAsync();
        var refreshed = await manualRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(refreshed.Ok, Is.True, $"Server refresh returned HTTP {refreshed.Status}.");
        await Expect(grid.Locator("a.server-name-link").Filter(new() { HasText = "build-01" }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Servers_NavigateFromSidebar_Works()
    {
        await NavigateToAsync("");
        var nav = Page.Locator(".rz-panel-menu");
        await nav.GetByText("Servers").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/servers"), new() { Timeout = 5000 });
    }
}
