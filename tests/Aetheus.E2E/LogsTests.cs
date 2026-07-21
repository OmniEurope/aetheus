// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Logs")]
public class LogsTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Logs_WatchLoadsSeededEntries_AndClearRemovesThem()
    {
        await NavigateToAsync("tasks");
        await WaitForDataGridAsync();
        var seededTaskRow = Page.Locator(".rz-data-grid-data tr")
            .Filter(new() { HasText = "Demo web diagnostic" });
        await Expect(seededTaskRow).ToBeVisibleAsync(new() { Timeout = 10000 });
        var taskIdText = (await seededTaskRow.Locator("td").First.InnerTextAsync()).Trim();
        Assert.That(int.TryParse(taskIdText, out var taskId), Is.True,
            $"Expected the first task cell to contain an id, got '{taskIdText}'.");

        await NavigateToAsync("logs");
        await Expect(Page.GetByText("Live Logs").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Live Logs"));
        var watchButton = Page.GetByRole(AriaRole.Button, new() { Name = "Watch" });
        await Expect(watchButton).ToBeVisibleAsync(new() { Timeout = 10000 });
        var clearButton = Page.GetByRole(AriaRole.Button, new() { Name = "Clear" });
        await Expect(clearButton).ToBeVisibleAsync(new() { Timeout = 10000 });
        // The Task Id filter is a RadzenNumeric, which renders as `.rz-numeric`.
        var taskIdInput = Page.Locator(".rz-numeric input");
        await Expect(taskIdInput.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var logPanel = Page.Locator(".live-log-panel");
        await Expect(logPanel).ToBeVisibleAsync();

        await taskIdInput.FillAsync(taskId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await taskIdInput.PressAsync("Tab");
        await Expect(watchButton).ToBeEnabledAsync();
        var historyResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains($"/api/logs/task/{taskId}", StringComparison.Ordinal));
        await watchButton.ClickAsync();
        var response = await historyResponse.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(response.Ok, Is.True, $"Task log history returned HTTP {response.Status}.");
        await Expect(Page.GetByText("Connected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(logPanel.Locator(".log-entry").Filter(new() { HasText = "Demo seed record" }))
            .ToBeVisibleAsync(new() { Timeout = 10000 });

        await clearButton.ClickAsync();
        await Expect(logPanel.Locator(".log-entry")).ToHaveCountAsync(0);
        await Expect(logPanel.GetByText("No log entries yet.", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Logs_NavigateFromSidebar_Works()
    {
        // "Logs" is a contextual sub-item of the Servers section, only rendered
        // while inside that section. Start from /servers so it is in the sidebar.
        await NavigateToAsync("servers");
        await ClickSidebarNavItemAsync("Logs");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/logs"), new() { Timeout = 5000 });
    }
}
