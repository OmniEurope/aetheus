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
    public async Task SignalRDrop_WithHealthyBackend_StaysUsableAndReconnectsAutomatically()
    {
        await NavigateToAsync("servers");
        var webServer = Page.Locator("a.server-name-link").Filter(new() { HasText = "web-01" }).First;
        var serverHref = await webServer.GetAttributeAsync("href");
        var serverMatch = Regex.Match(serverHref ?? string.Empty, @"/servers/(\d+)/");
        Assert.That(serverMatch.Success, Is.True, "The seeded web-01 server must expose its id.");
        var serverId = int.Parse(serverMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        await ReenrollServerForTaskProbeAsync(serverId);

        AllowBrowserDiagnostic(new Regex(
            @"ERR_FAILED|Failed to (?:start|complete)|WebSocket.*failed|Failed to fetch",
            RegexOptions.IgnoreCase));
        var dialog = Page.Locator(".connection-lost-mask");
        await Expect(dialog).ToHaveCountAsync(0);

        const string taskHubPattern = "**/hubs/servers**";
        var blockedHubRequests = 0;
        await Page.RouteAsync(taskHubPattern, route =>
        {
            Interlocked.Increment(ref blockedHubRequests);
            return route.AbortAsync();
        });
        try
        {
            // Reloading keeps the independently served WASM frontend available while
            // the real task-tracker SignalR negotiate/connect calls fail at the network
            // layer. A healthy HTTP backend must keep the application usable instead of
            // surfacing the blocking backend-offline overlay.
            await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            // Wait for the condition, not for a guessed duration. A flat 2.5s budget assumed the WASM
            // frontend had booted and opened its first hub call by then; on a loaded agent it had not,
            // the counter was still 0, and the run failed with "Expected: greater than 0" even though
            // nothing was broken. The assertion below is unchanged and still blocking - only the wait
            // became deterministic.
            var hubCallDeadline = DateTime.UtcNow.AddSeconds(20);
            while (Volatile.Read(ref blockedHubRequests) == 0 && DateTime.UtcNow < hubCallDeadline)
                await Page.WaitForTimeoutAsync(250);

            Assert.That(Volatile.Read(ref blockedHubRequests), Is.GreaterThan(0),
                "The browser must have exercised a blocked SignalR request.");
            await Expect(dialog).ToHaveCountAsync(0);
        }
        finally
        {
            await Page.UnrouteAsync(taskHubPattern);
        }

        // Restoring the real SignalR route lets the initial-connect retry loop recover without
        // a reload or manual action.
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

            // The budget must cover the reconnect backoff, not a guessed delay. Blocking the hub
            // walks FrontendRuntimeDefaults.SignalRRetryDelays up to its 30s step, so the tracker
            // can legitimately stay disconnected for 30s after the route is restored - TaskQueued
            // is then missed and only the SeedActiveAsync of the next successful connect brings the
            // task back. A 10s budget was shorter than that worst case and failed the run with
            // "element(s) not found" while nothing was broken. The probe task never terminates on
            // its own (no real agent answers it), so waiting longer cannot mask a lost event: the
            // badge is owed to us for as long as the task stays in flight.
            await Expect(Page.Locator(".task-tracker-badge")).ToBeVisibleAsync(new() { Timeout = 45000 });

            // The overlay can come back once more here: SignalR backs off between reconnect
            // attempts, so the longer the hub stayed blocked the later the successful retry lands.
            // Clicking through it failed with "connection-lost-mask intercepts pointer events".
            // Re-asserting its absence is stricter than ignoring it - the click below still has to
            // happen on a genuinely reconnected page.
            await Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 30000 });
            await Page.Locator(".task-tracker-btn").ClickAsync();
            await Expect(Page.Locator(".task-tracker-row-name").Filter(new() { HasText = taskName }))
                .ToBeVisibleAsync(new() { Timeout = 10000 });
        }
        finally
        {
            if (taskId > 0)
            {
                await AssertTaskCancelledOrTerminalAsync(
                    taskId,
                    "The reconnect probe task must be cancelled or already terminal.");
            }
        }
    }
}
