// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Aetheus.E2E;

[Category("E2E")]
[Category("Servers")]
public sealed class ServerInteractiveControlsTests : E2ETestBase
{
    private int _serverId;

    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
        await NavigateToAsync("servers");
        var server = Page.Locator("a.server-name-link").Filter(new() { HasText = "web-01" });
        await Expect(server).ToBeVisibleAsync(new() { Timeout = 10000 });
        var href = await server.GetAttributeAsync("href");
        var match = Regex.Match(href ?? string.Empty, @"/servers/(\d+)/");
        Assert.That(match.Success, Is.True, "The seeded web-01 server must expose its detail URL.");
        _serverId = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public async Task Docker_AutoRefreshPruneAndRelationFilter_TriggerTheirSideEffects()
    {
        await NavigateToAsync($"servers/{_serverId}/docker");
        var autoRefresh = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Auto-refresh", Exact = true });
        await Expect(autoRefresh).ToBeVisibleAsync(new() { Timeout = 10000 });

        var refreshResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.EndsWith($"/api/servers/{_serverId}/docker/containers", StringComparison.Ordinal));
        await Page.GetByText("Auto-refresh", new() { Exact = true }).ClickAsync();
        await Expect(autoRefresh).ToBeCheckedAsync();
        var dockerResponse = await refreshResponse.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.That(dockerResponse.Ok, Is.True, $"Docker auto-refresh returned HTTP {dockerResponse.Status}.");
        // Match the container row, not the bare text: the relations graph renders the same name as
        // an SVG label, so GetByText resolves to two elements and trips Playwright strict mode.
        await Expect(Page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex("toto-web") }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.GetByText("Auto-refresh", new() { Exact = true }).ClickAsync();
        await Expect(autoRefresh).Not.ToBeCheckedAsync();

        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Prune$") }).First.ClickAsync();
        var pruneDialog = Page.GetByRole(AriaRole.Dialog, new()
        {
            Name = "Prune Docker Resources",
            Exact = true
        });
        await Expect(pruneDialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        var pruneChecks = pruneDialog.GetByRole(AriaRole.Checkbox);
        await Expect(pruneChecks).ToHaveCountAsync(4);
        await pruneDialog.GetByText("Select All", new() { Exact = true }).ClickAsync();
        for (var index = 0; index < 4; index++)
            await Expect(pruneChecks.Nth(index)).Not.ToBeCheckedAsync();
        await Expect(pruneDialog.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Prune$") }))
            .ToBeDisabledAsync();
        await pruneDialog.GetByRole(AriaRole.Button, new()
        {
            Name = "Close dialog",
            Exact = true
        }).ClickAsync();
        await Expect(pruneDialog).ToHaveCountAsync(0, new() { Timeout = 5000 });

        await Page.GetByRole(AriaRole.Tab)
            .Filter(new() { Has = Page.GetByText("Relations", new() { Exact = true }) })
            .ClickAsync();
        // TabRenderMode.Client keeps inactive panels in the DOM, so the graph nodes exist even
        // before the tab opens. Wait for the canvas itself to be shown first: asserting on a node
        // straight away reports "hidden" whether the tab failed to open or the graph has not been
        // laid out yet, which tells us nothing about which one happened.
        var graphSvg = Page.Locator(".docker-graph-svg");
        await Expect(graphSvg).ToBeVisibleAsync(new() { Timeout = 10000 });
        var graphNodes = graphSvg.Locator("[data-node]");
        await Expect(graphNodes.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var initialNodeCount = await graphNodes.CountAsync();
        var containersFilter = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Containers", Exact = true });
        await Page.GetByText("Containers", new() { Exact = true }).Last.ClickAsync();
        await Expect(containersFilter).Not.ToBeCheckedAsync();
        await Expect(graphNodes).ToHaveCountAsync(initialNodeCount - 2, new() { Timeout = 5000 });
    }

    [Test]
    public async Task Services_FollowToggle_RestartsLogRequestWithFollowEnabled()
    {
        await ReenrollServerForTaskProbeAsync(_serverId);
        await NavigateToAsync($"servers/{_serverId}/services");
        var logsButton = Page.Locator("button[title='View logs']").First;
        await Expect(logsButton).ToBeVisibleAsync(new() { Timeout = 10000 });
        var taskIds = new List<int>();
        try
        {
            var initialRequest = Page.WaitForResponseAsync(response =>
                response.Request.Method == "POST"
                && response.Url.EndsWith($"/api/servers/{_serverId}/services/logs", StringComparison.Ordinal));
            await logsButton.ClickAsync();
            var initialResponse = await initialRequest.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(initialResponse.Ok, Is.True, $"Initial service-log request returned HTTP {initialResponse.Status}.");
            taskIds.Add(ReadTaskId(await initialResponse.TextAsync()));
            var renderedLogTitle = Page.GetByText(new Regex("^Logs - ")).Last;
            await Expect(renderedLogTitle).ToBeVisibleAsync(new() { Timeout = 5000 });
            var expectedTaskName = (await renderedLogTitle.InnerTextAsync()).Trim();

            var trackerBadge = Page.Locator(".task-tracker-badge");
            await Expect(trackerBadge).ToBeVisibleAsync(new() { Timeout = 10000 });
            var initialActiveCount = int.Parse(
                (await trackerBadge.InnerTextAsync()).Trim(),
                System.Globalization.CultureInfo.InvariantCulture);

            var follow = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Follow", Exact = true });
            await Expect(follow).ToBeVisibleAsync(new() { Timeout = 10000 });
            var followRequest = Page.WaitForResponseAsync(response =>
                response.Request.Method == "POST"
                && response.Url.EndsWith($"/api/servers/{_serverId}/services/logs", StringComparison.Ordinal)
                && (response.Request.PostData?.Contains("\"follow\":true", StringComparison.OrdinalIgnoreCase) ?? false));
            await Page.GetByText("Follow", new() { Exact = true }).ClickAsync();
            await Expect(follow).ToBeCheckedAsync();
            var followResponse = await followRequest.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(followResponse.Ok, Is.True, $"Follow service-log request returned HTTP {followResponse.Status}.");
            taskIds.Add(ReadTaskId(await followResponse.TextAsync()));

            await Expect(trackerBadge).ToHaveTextAsync(
                (initialActiveCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                new() { Timeout = 10000 });
            await Page.Locator(".task-tracker-btn").ClickAsync();
            var renderedLogTasks = Page.Locator(".task-tracker-row-name")
                .Filter(new() { HasText = expectedTaskName });
            await Expect(renderedLogTasks).ToHaveCountAsync(2, new() { Timeout = 10000 });
        }
        finally
        {
            foreach (var taskId in taskIds)
            {
                await AssertTaskCancelledOrTerminalAsync(
                    taskId,
                    $"Service-log probe task {taskId} must be cancelled or already terminal.");
            }
        }
    }

    private static int ReadTaskId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("taskId").GetInt32();
    }
}
