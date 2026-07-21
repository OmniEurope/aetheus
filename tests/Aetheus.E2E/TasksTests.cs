// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Tasks")]
public class TasksTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Tasks_PageRendersExpectedGridAndColumns()
    {
        await NavigateToAsync("tasks");
        await Expect(Page.GetByText("Tasks").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Tasks"));
        await WaitForNoSpinnerAsync();
        await WaitForDataGridAsync();

        var grid = Page.Locator(".rz-datatable");
        await Expect(grid).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Radzen 10 RadzenDataGrid renders one `.rz-column-title` per column header.
        var headers = Page.Locator(".rz-column-title");
        await Expect(headers.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var count = await headers.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(6), "Tasks grid should have at least 6 columns");

        var webDiagnostic = Page.Locator(".rz-data-grid-data tr")
            .Filter(new() { HasText = "Demo web diagnostic" });
        await Expect(webDiagnostic).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(webDiagnostic.GetByText("Cancelled", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        var buildDiagnostic = Page.Locator(".rz-data-grid-data tr")
            .Filter(new() { HasText = "Demo build diagnostic" });
        await Expect(buildDiagnostic).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(buildDiagnostic.GetByText("Cancelled", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Tasks_NavigateFromSidebar_Works()
    {
        // "Tasks" is a contextual sub-item of the Servers section, only rendered
        // while inside that section. Start from /servers so it is in the sidebar.
        await NavigateToAsync("servers");
        await ClickSidebarNavItemAsync("Tasks");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/tasks"), new() { Timeout = 5000 });
    }
}
