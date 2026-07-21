// SPDX-License-Identifier: EUPL-1.2
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
                await Page.WaitForSelectorAsync("input[name='Username']", new() { Timeout = AppReadyTimeoutMs });

                await Page.FillAsync("input[name='Username']", AdminUser);
                await Page.FillAsync("input[name='Password']", AdminPassword);
                await Page.ClickAsync("button[type='submit']");

                var submitStateHandle = await Page.WaitForFunctionAsync("""
                    () => {
                        if (location.pathname === '/') return 'root';
                        if (document.querySelector('.login-container .rz-alert-danger, .login-container .rz-alert'))
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
                        const panel = document.querySelector('.rz-panel-menu');
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
        // back to the historical Radzen-class / username-field selector for that route.
        await Page.WaitForSelectorAsync(
            $"[data-testid='{PlaywrightConfig.BlazorReadyTestId}'], .rz-panel-menu, input[name='Username']",
            new() { Timeout = AppReadyTimeoutMs });

        // Wait for the loading spinner to clear rather than sleeping a fixed 300ms. Hidden also
        // succeeds when the selector is absent, while a spinner that remains visible times out.
        await WaitForNoSpinnerAsync();
    }

    protected async Task WaitForBlazorAsync()
    {
        await WaitForAppReadyAsync();
    }

    protected async Task WaitForDataGridAsync()
    {
        // Radzen 10 renders RadzenDataGrid with `.rz-data-grid` (and `.rz-datatable`).
        await Page.WaitForSelectorAsync(".rz-data-grid, .rz-datatable", new() { Timeout = AppReadyTimeoutMs });
    }

    protected async Task WaitForNoSpinnerAsync()
    {
        // Hidden also succeeds when the selector is absent, so no exception handling is
        // needed for the normal "spinner never appeared" case. A real 30-second timeout
        // means the page stayed busy and must fail here instead of being silently ignored.
        await Page.WaitForSelectorAsync(".rz-progressbar-circular", new()
        {
            State = WaitForSelectorState.Hidden,
            Timeout = AppReadyTimeoutMs
        });
    }

    // Radzen 10 RadzenPanelMenu renders each item as
    // `a.rz-navigation-item-link > span.rz-navigation-item-text`. Match the
    // text span by exact text (so "Logs" doesn't also match "System Logs"),
    // then return the clickable link ancestor.
    protected ILocator SidebarNavItem(string text)
    {
        return Page.Locator(".rz-panel-menu a.rz-navigation-item-link")
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
