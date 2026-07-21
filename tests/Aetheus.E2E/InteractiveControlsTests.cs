// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Aetheus.E2E;

[Category("E2E")]
[Category("Security")]
public sealed class InteractiveControlsTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task OwnerType_RadioTextClick_RevealsAndHidesProjectSelector()
    {
        await NavigateToAsync("vaults/new");

        var projectText = Page.GetByText("Project", new() { Exact = true }).First;
        await Expect(projectText).ToBeVisibleAsync(new() { Timeout = 10000 });
        await projectText.ClickAsync();

        var projectRadio = Page.GetByRole(AriaRole.Radio, new() { Name = "Project", Exact = true });
        await Expect(projectRadio).ToBeCheckedAsync(new() { Timeout = 5000 });
        var projectSelector = Page.Locator(".rz-dropdown");
        await Expect(projectSelector).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click the visible radio text, not the input circle. This is the user
        // interaction that previously had only been inferred from Radzen markup.
        var globalText = Page.GetByText("Global", new() { Exact = true });
        await Expect(globalText).ToBeVisibleAsync();
        await globalText.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Global", Exact = true }))
            .ToBeCheckedAsync(new() { Timeout = 5000 });
        await Expect(projectSelector).ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    [Test]
    public async Task PasswordRuleFeedback_UpdatesOnInputWithoutBlur()
    {
        await NavigateToAsync("users");
        var adminLink = Page.Locator("a.server-name-link")
            .Filter(new() { HasText = AdminUser })
            .First;
        await Expect(adminLink).ToBeVisibleAsync(new() { Timeout = 10000 });
        await adminLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/users/\d+$"), new() { Timeout = 10000 });

        var securityTab = Page.GetByRole(AriaRole.Tab)
            .Filter(new() { Has = Page.GetByText("Security", new() { Exact = true }) });
        await Expect(securityTab).ToBeVisibleAsync(new() { Timeout = 10000 });
        await securityTab.ClickAsync();

        var password = Page.Locator("input[type='password']").First;
        var feedback = Page.GetByText("Password must be at least 12 characters.", new() { Exact = true });
        await Expect(password).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(feedback).ToHaveClassAsync(new Regex("rz-color-secondary"));

        await password.FillAsync("123456789012");

        await Expect(password).ToBeFocusedAsync();
        await Expect(feedback).ToHaveClassAsync(new Regex("rz-color-success"), new() { Timeout = 5000 });
    }

    [Test]
    public async Task ConnectionLossDialog_AppearsAfterRealBrowserNetworkDrop_AndClosesAfterReconnect()
    {
        await NavigateToAsync("servers");
        var webServer = Page.Locator("a.server-name-link").Filter(new() { HasText = "web-01" }).First;
        var serverHref = await webServer.GetAttributeAsync("href");
        var serverMatch = Regex.Match(serverHref ?? string.Empty, @"/servers/(\d+)/");
        Assert.That(serverMatch.Success, Is.True, "The seeded web-01 server must expose its id.");
        var serverId = int.Parse(serverMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        AllowBrowserDiagnostic(new Regex(
            @"ERR_FAILED|Failed to (?:start|complete)|WebSocket.*failed|Failed to fetch",
            RegexOptions.IgnoreCase));
        var dialog = Page.Locator(".connection-lost-mask");
        await Expect(dialog).ToHaveCountAsync(0);

        const string taskHubPattern = "**/hubs/servers**";
        await Page.RouteAsync(taskHubPattern, route => route.AbortAsync());
        try
        {
            // Reloading keeps the independently served WASM frontend available while
            // the real task-tracker SignalR negotiate/connect calls fail at the network
            // layer. MainLayout must surface the blocking overlay after its grace period.
            await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Expect(dialog.GetByRole(AriaRole.Button)).ToBeVisibleAsync(new() { Timeout = 5000 });
        }
        finally
        {
            await Page.UnrouteAsync(taskHubPattern);
        }

        // Restoring the real SignalR route can reconnect immediately. The overlay is then
        // removed by the product, so clicking its transient button after UnrouteAsync races
        // a correct reconnect and can target a detached Radzen element.
        await Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 15000 });
        await WaitForNoSpinnerAsync();

        // Prove that the restored connection transports a real server event, not only that the
        // overlay disappeared: task creation broadcasts TaskQueued and the header tracker renders it.
        var taskName = $"e2e-reconnect-{Guid.NewGuid():N}";
        var taskId = 0;
        try
        {
            taskId = await Page.EvaluateAsync<int>(
                """
                async ({ backendUrl, serverId, taskName }) => {
                    const token = localStorage.getItem('aetheus_auth_token');
                    const response = await fetch(`${backendUrl}/api/tasks`, {
                        method: 'POST',
                        headers: {
                            'Authorization': `Bearer ${token}`,
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            serverId,
                            name: taskName,
                            command: 'echo e2e-reconnect',
                            executor: 'Shell',
                            timeoutSeconds: 30,
                            environmentVariables: {}
                        })
                    });
                    if (!response.ok) throw new Error(`Task creation failed: HTTP ${response.status}`);
                    return (await response.json()).id;
                }
                """,
                new { backendUrl = BackendUrl, serverId, taskName });

            await Expect(Page.Locator(".task-tracker-badge")).ToBeVisibleAsync(new() { Timeout = 10000 });
            await Page.Locator(".task-tracker-btn").ClickAsync();
            await Expect(Page.Locator(".task-tracker-row-name").Filter(new() { HasText = taskName }))
                .ToBeVisibleAsync(new() { Timeout = 10000 });
        }
        finally
        {
            if (taskId > 0)
            {
                var cancelStatus = await Page.EvaluateAsync<int>(
                    """
                    async ({ backendUrl, taskId }) => {
                        const token = localStorage.getItem('aetheus_auth_token');
                        const response = await fetch(`${backendUrl}/api/tasks/${taskId}/cancel`, {
                            method: 'POST',
                            headers: { 'Authorization': `Bearer ${token}` }
                        });
                        return response.status;
                    }
                    """,
                    new { backendUrl = BackendUrl, taskId });
                Assert.That(cancelStatus, Is.EqualTo(200), "The reconnect probe task must be cancelled.");
            }
        }
    }
}
