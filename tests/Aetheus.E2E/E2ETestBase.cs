// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;

namespace Aetheus.E2E;

public abstract class E2ETestBase : PageTest
{
    private readonly List<string> _browserDiagnostics = [];
    private readonly List<Regex> _allowedBrowserDiagnostics = [];

    // All harness configuration (URLs, admin creds, timeouts, readiness hook) lives in
    // PlaywrightConfig - each value env-overridable with the historical default. These
    // thin accessors keep the existing test call sites (FrontendUrl, AdminUser, …) intact.
    protected static string FrontendUrl => PlaywrightConfig.FrontendUrl;
    protected static string BackendUrl => PlaywrightConfig.BackendUrl;
    protected static string AdminUser => PlaywrightConfig.AdminUser;
    protected static string AdminPassword => PlaywrightConfig.AdminPassword;

    private static int AppReadyTimeoutMs => PlaywrightConfig.AppReadyTimeoutMs;

    protected virtual bool UsePreauthenticatedContext => true;

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            Locale = "en-US"
        };
        if (UsePreauthenticatedContext)
            options.StorageState = E2EAuthSession.StorageStateJson;
        return options;
    }

    [SetUp]
    public void CaptureBrowserDiagnostics()
    {
        _browserDiagnostics.Clear();
        _allowedBrowserDiagnostics.Clear();
        Page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                var location = string.IsNullOrWhiteSpace(message.Location)
                    ? string.Empty
                    : $" [{message.Location}]";
                _browserDiagnostics.Add($"console error: {message.Text}{location}");
            }
        };
        Page.PageError += (_, exception) => _browserDiagnostics.Add($"page error: {exception}");
    }

    /// <summary>
    /// Allows one explicit diagnostic pattern for a test that deliberately breaks a browser resource.
    /// The exception is scoped to that test; every other console/page error still fails the teardown.
    /// </summary>
    protected void AllowBrowserDiagnostic(Regex pattern) => _allowedBrowserDiagnostics.Add(pattern);

    protected async Task LoginAsync(bool force = false)
    {
        if (UsePreauthenticatedContext && !force)
            return;

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await Page.GotoAsync($"{FrontendUrl}/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await Page.WaitForSelectorAsync("#Username", new() { Timeout = AppReadyTimeoutMs });

                await Page.FillAsync("#Username", AdminUser);
                await Page.FillAsync("#Password", AdminPassword);
                await Page.ClickAsync("button[type='submit']");

                var submitStateHandle = await Page.WaitForFunctionAsync("""
                    () => {
                        if (location.pathname === '/') return 'root';
                        if (document.querySelector('.login-container .omni-alert--danger, .login-container .omni-alert'))
                            return 'login-error';
                        return null;
                    }
                    """, null, new() { Timeout = AppReadyTimeoutMs });
                var submitState = await submitStateHandle.JsonValueAsync<string>();
                if (submitState == "login-error")
                {
                    if (attempt == maxAttempts)
                        throw new InvalidOperationException($"Login failed after {maxAttempts} attempts.");
                    TestContext.Progress.WriteLine($"Login returned an authentication error; retrying attempt {attempt + 1}/{maxAttempts}.");
                    continue;
                }

                // The redirect to "/" fires before the authenticated shell is ready. A reset E2E
                // database can also invalidate a just-issued token while stale authenticated requests
                // finish draining; in that explicit case MainLayout sends us back to /login. Detect the
                // two terminal states instead of waiting 30 seconds for a panel that cannot appear, and
                // retry the complete login flow a bounded number of times.
                var stateHandle = await Page.WaitForFunctionAsync("""
                    () => {
                        const panel = document.querySelector('.omni-panel-menu');
                        if (panel && getComputedStyle(panel).visibility !== 'hidden') return 'ready';
                        if (location.pathname === '/login') return 'login';
                        return null;
                    }
                    """, null, new() { Timeout = AppReadyTimeoutMs });
                var state = await stateHandle.JsonValueAsync<string>();

                if (state == "ready")
                    return;

                if (attempt == maxAttempts)
                    throw new InvalidOperationException($"Login returned to /login after {maxAttempts} attempts.");
            }
            catch (PlaywrightException exception) when (
                attempt < maxAttempts &&
                exception.Message.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal))
            {
                TestContext.Progress.WriteLine(
                    $"Login navigation hit ERR_NETWORK_CHANGED; retrying attempt {attempt + 1}/{maxAttempts}: {exception.Message}");
            }
        }
    }

    protected async Task NavigateToAsync(string path)
    {
        await Page.GotoAsync($"{FrontendUrl}/{path.TrimStart('/')}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForAppReadyAsync();
    }

    // Replaces a brittle NetworkIdle wait: the app keeps a SignalR WebSocket
    // open, so the network is rarely "idle". Instead wait for the Blazor app to
    // become interactive - the authenticated shell renders the side nav, the
    // login page renders the username field.
    private async Task WaitForAppReadyAsync()
    {
        // Prefer the stable readiness hook on MainLayout's top-level container - it only
        // renders once the boot gate (auth/orgs/permissions) resolves, so it's a
        // deterministic "chrome is up" signal. The login page renders no chrome, so fall
        // back to the menu / username-field selector for that route.
        await Page.WaitForSelectorAsync(
            $"[data-testid='{PlaywrightConfig.BlazorReadyTestId}'], .omni-panel-menu, #Username",
            new() { State = WaitForSelectorState.Attached, Timeout = AppReadyTimeoutMs });

        // Wait for every page-level loading indicator to clear rather than sleeping a fixed delay.
        // Task-tracker loaders are intentionally non-centered and do not block page readiness.
        await WaitForNoSpinnerAsync();
    }

    protected async Task WaitForBlazorAsync()
    {
        await WaitForAppReadyAsync();
    }

    protected async Task WaitForDataGridAsync()
    {
        await Page.WaitForSelectorAsync(".omni-data-grid", new() { Timeout = AppReadyTimeoutMs });
    }

    protected async Task WaitForNoSpinnerAsync()
    {
        await Page.WaitForFunctionAsync("""
            () => [...document.querySelectorAll('.aetheus-loader')]
                .every(element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display === 'none' || style.visibility === 'hidden' ||
                        rect.width === 0 || rect.height === 0;
                })
            """, null, new() { Timeout = AppReadyTimeoutMs });
    }

    // Match the menu item's exact text so "Logs" does not also match "System Logs".
    protected ILocator SidebarNavItem(string text)
    {
        return Page.Locator(".omni-panel-menu a.omni-panel-menu__link, .rz-panel-menu a.rz-navigation-item-link")
            .Filter(new()
            {
                Has = Page.GetByText(text, new() { Exact = true })
            });
    }

    protected async Task ClickSidebarNavItemAsync(string text)
    {
        var item = SidebarNavItem(text).First;
        await Expect(item).ToBeVisibleAsync(new() { Timeout = AppReadyTimeoutMs });
        await item.ClickAsync();
    }

    /// <summary>
    /// Cancels an E2E probe task, or proves that the agent completed it before cleanup won the race.
    /// The cancel endpoint deliberately returns 404 for terminal tasks, so treating every 404 as a
    /// missing task makes otherwise successful realtime tests depend on agent scheduling speed.
    /// </summary>
    protected async Task AssertTaskCancelledOrTerminalAsync(int taskId, string message)
    {
        var result = await Page.EvaluateAsync<JsonElement>(
            """
            async ({ backendUrl, taskId }) => {
                const token = localStorage.getItem('aetheus_auth_token');
                const headers = { 'Authorization': `Bearer ${token}` };
                const cancelResponse = await fetch(`${backendUrl}/api/tasks/${taskId}/cancel`, {
                    method: 'POST',
                    headers
                });

                if (cancelResponse.status === 200)
                    return { cancelStatus: 200, taskFetchStatus: null, taskStatus: null };

                const taskResponse = await fetch(`${backendUrl}/api/tasks/${taskId}`, { headers });
                const task = taskResponse.ok ? await taskResponse.json() : null;
                return {
                    cancelStatus: cancelResponse.status,
                    taskFetchStatus: taskResponse.status,
                    taskStatus: task?.status ?? null
                };
            }
            """,
            new { backendUrl = BackendUrl, taskId });

        var cancelStatus = result.GetProperty("cancelStatus").GetInt32();
        if (cancelStatus == 200)
            return;

        Assert.Multiple(() =>
        {
            Assert.That(cancelStatus, Is.EqualTo(404), message);
            Assert.That(result.GetProperty("taskFetchStatus").GetInt32(), Is.EqualTo(200),
                $"{message} The task must still exist when cancellation loses the terminal-state race.");
            Assert.That(result.GetProperty("taskStatus").GetString(),
                Is.AnyOf("Success", "Failed", "Timeout", "Cancelled"),
                $"{message} A rejected cancellation is valid only for an already terminal task.");
        });
    }

    /// <summary>
    /// Re-enrolls a seeded server through the real registration-token and public agent enrollment
    /// endpoints. Task-producing browser probes therefore exercise the production online-agent
    /// guard without a test-only database mutation or a weakened backend check.
    /// </summary>
    protected async Task ReenrollServerForTaskProbeAsync(int serverId)
    {
        var result = await Page.EvaluateAsync<JsonElement>(
            """
            async ({ backendUrl, serverId }) => {
                const token = localStorage.getItem('aetheus_auth_token');
                const authHeaders = {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                };
                const serverResponse = await fetch(`${backendUrl}/api/servers/${serverId}`, {
                    headers: { 'Authorization': `Bearer ${token}` }
                });
                const server = serverResponse.ok ? await serverResponse.json() : null;
                const tokenResponse = await fetch(`${backendUrl}/api/auth/registration-tokens`, {
                    method: 'POST',
                    headers: authHeaders,
                    body: JSON.stringify({ expirationHours: 1 })
                });
                const registration = tokenResponse.ok ? await tokenResponse.json() : null;
                const enrollmentResponse = server && registration
                    ? await fetch(`${backendUrl}/api/auth/register`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            registrationToken: registration.token,
                            hostname: server.hostname,
                            osDescription: server.osDescription || 'Linux E2E',
                            agentVersion: 'e2e-task-probe',
                            ipAddress: server.ipAddress || '127.0.0.1',
                            agentInstalledAt: new Date().toISOString(),
                            dockerAvailable: server.dockerAvailable ?? true,
                            pipelineRunnerAvailable: server.pipelineRunnerEnabled ?? true,
                            insecureTls: server.insecureTls ?? false
                        })
                    })
                    : null;
                return {
                    serverStatus: serverResponse.status,
                    tokenStatus: tokenResponse.status,
                    enrollmentStatus: enrollmentResponse?.status ?? 0
                };
            }
            """,
            new { backendUrl = BackendUrl, serverId });

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("serverStatus").GetInt32(), Is.EqualTo(200));
            Assert.That(result.GetProperty("tokenStatus").GetInt32(), Is.EqualTo(200));
            Assert.That(result.GetProperty("enrollmentStatus").GetInt32(), Is.EqualTo(200));
        });
    }

    // On failure, capture a screenshot plus the page URL/title so the harness
    // log shows *what* the page actually rendered (e.g. stuck on /login,
    // error boundary, blank app) instead of only an opaque timeout.
    [TearDown]
    public async Task CaptureOnFailureAsync()
    {
        var unexpectedDiagnostics = _browserDiagnostics
            .Where(diagnostic => !_allowedBrowserDiagnostics.Any(pattern => pattern.IsMatch(diagnostic)))
            .ToArray();
        var testAlreadyFailed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        if (!testAlreadyFailed && unexpectedDiagnostics.Length == 0)
            return;

        try
        {
            var safeName = Regex.Replace(TestContext.CurrentContext.Test.Name, "[^A-Za-z0-9_]", "_");
            var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "e2e-failures");
            Directory.CreateDirectory(dir);
            var shot = Path.Combine(dir, $"{safeName}.png");
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
            var diagnostics = Path.Combine(dir, $"{safeName}.log");
            await File.WriteAllLinesAsync(diagnostics, _browserDiagnostics);
            TestContext.Out.WriteLine($"[E2E-DIAG] {safeName}: url='{Page.Url}' title='{await Page.TitleAsync()}' screenshot='{shot}' diagnostics='{diagnostics}'");
        }
        catch (PlaywrightException)
        {
            // Diagnostics are best-effort.
        }

        if (!testAlreadyFailed && unexpectedDiagnostics.Length > 0)
        {
            Assert.Fail(
                "Unexpected browser errors were captured:\n  "
                + string.Join("\n  ", unexpectedDiagnostics));
        }
    }
}
