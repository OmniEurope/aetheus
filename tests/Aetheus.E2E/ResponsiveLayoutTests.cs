// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Playwright;

namespace Aetheus.E2E;

/// <summary>Guards the small-screen contract: content scrolls inside its component, never the page.</summary>
[Category("E2E")]
[Category("Dashboard")]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ResponsiveLayoutTests : E2ETestBase
{
    private static readonly string[] PrimaryMobilePaths =
    [
        "/",
        "/servers",
        "/projects",
        "/pipelines",
        "/releases",
        "/environments",
        "/tasks",
        "/alerts",
        "/backups",
        "/vaults",
        "/variable-libraries",
        "/service-connections",
        "/git-repositories",
        "/logs",
        "/settings"
    ];

    private static readonly string[] SecondaryMobilePaths =
    [
        "/servers/add-agent",
        "/servers/1",
        "/servers/1/overview",
        "/servers/1/configuration",
        "/servers/1/docker",
        "/servers/1/services",
        "/servers/1/tasks",
        "/servers/1/apps",
        "/servers/1/modules",
        "/servers/1/logs",
        "/servers/1/apache",
        "/servers/1/certbot",
        "/servers/1/cron",
        "/servers/1/firewall",
        "/servers/1/libraries",
        "/servers/1/mail",
        "/servers/1/pipelines",
        "/servers/1/portsentry",
        "/servers/1/projects",
        "/servers/1/properties",
        "/servers/1/releases",
        "/servers/1/rkhunter",
        "/servers/1/teamspeak",
        "/servers/1/updates",
        "/servers/1/vaults",
        "/projects/new",
        "/projects/3",
        "/projects/3/overview",
        "/projects/3/board",
        "/projects/3/edit",
        "/projects/3/artifacts",
        "/projects/3/environments",
        "/projects/3/external-repo",
        "/projects/3/libraries",
        "/projects/3/logs",
        "/projects/3/monitoring",
        "/projects/3/pipelines",
        "/projects/3/quality",
        "/projects/3/releases",
        "/projects/3/servers",
        "/projects/3/tasks",
        "/projects/3/vaults",
        "/pipelines/new",
        "/pipelines/1",
        "/pipelines/runs/1",
        "/environments/new",
        "/environments/1",
        "/vaults/new",
        "/vaults/1",
        "/variable-libraries/new",
        "/variable-libraries/1",
        "/releases/1",
        "/dashboards",
        "/admin",
        "/users",
        "/users/new",
        "/users/1",
        "/admin/roles",
        "/admin/roles/new",
        "/admin/roles/1",
        "/admin/organizations",
        "/admin/organizations/new",
        "/admin/organizations/1",
        "/admin/organizations/1/edit",
        "/admin/settings",
        "/admin/audit",
        "/admin/plugins",
        "/admin/notifications",
        "/admin/package-feeds",
        "/admin/system-logs",
        "/api-reference",
        "/help",
        "/help/dashboard"
    ];

    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task PrimaryMobilePages_RemainReadableInsideTheViewport()
        => await AuditMobilePathsAsync(PrimaryMobilePaths, 375);

    [Test]
    public async Task ServerMobilePages_RemainReadableInsideTheViewport()
        => await AuditMobilePathsAsync(SecondaryMobilePaths.Where(path => path.StartsWith("/servers/", StringComparison.Ordinal)), 375);

    [Test]
    public async Task ProjectMobilePages_RemainReadableInsideTheViewport()
        => await AuditMobilePathsAsync(SecondaryMobilePaths.Where(path => path.StartsWith("/projects/", StringComparison.Ordinal)), 375);

    [Test]
    [Category("ResponsiveAdmin")]
    public async Task AdministrationMobilePages_RemainReadableInsideTheViewport()
        => await AuditMobilePathsAsync(SecondaryMobilePaths.Where(path =>
            path == "/admin" || path.StartsWith("/admin/", StringComparison.Ordinal) || path.StartsWith("/users", StringComparison.Ordinal)), 375);

    [Test]
    public async Task OtherSecondaryMobilePages_RemainReadableInsideTheViewport()
    {
        var artifactPath = await ResolveSeededArtifactPathAsync();
        var paths = SecondaryMobilePaths.Where(path =>
            !path.StartsWith("/servers/", StringComparison.Ordinal)
            && !path.StartsWith("/projects/", StringComparison.Ordinal)
            && path != "/admin"
            && !path.StartsWith("/admin/", StringComparison.Ordinal)
            && !path.StartsWith("/users", StringComparison.Ordinal))
            .Append(artifactPath);
        await AuditMobilePathsAsync(paths, 375);
    }

    private async Task<string> ResolveSeededArtifactPathAsync()
    {
        await NavigateToAsync("/projects/3/artifacts");
        await WaitForNoSpinnerAsync();
        var artifactLink = Page.Locator("a[href^='/artifacts/']").First;
        await Expect(artifactLink).ToBeVisibleAsync();
        return await artifactLink.GetAttributeAsync("href")
            ?? throw new InvalidOperationException("The seeded artifact link has no href.");
    }

    [Test]
    public async Task RepresentativePages_RemainReadableAcrossCommonWidths()
    {
        var representativePaths = new[] { "/", "/servers", "/projects", "/pipelines", "/settings" };
        foreach (var viewportWidth in new[] { 390, 430 })
            await AuditMobilePathsAsync(representativePaths, viewportWidth);
    }

    [Test]
    public async Task TabletBand_UsesCompactOverlayAtEveryBoundaryWidth()
    {
        var representativePaths = new[] { "/", "/admin/audit", "/pipelines/1" };
        foreach (var viewportWidth in new[] { 767, 769, 800, 1024 })
            await AuditMobilePathsAsync(representativePaths, viewportWidth);
    }

    [Test]
    public async Task AboveTabletBreakpoint_RestoresDesktopRail()
    {
        await Page.SetViewportSizeAsync(1025, 812);
        await NavigateToAsync("/");
        await WaitForNoSpinnerAsync();

        var sidebar = Page.Locator(".rz-sidebar");
        await Expect(sidebar).ToBeVisibleAsync();
        await Expect(sidebar).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("rz-sidebar-collapsed"));
        var width = await sidebar.EvaluateAsync<double>("element => element.getBoundingClientRect().width");
        Assert.That(width, Is.GreaterThan(200));
        await Expect(Page.Locator(".sidebar-backdrop")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task MobileColdLoad_NeverPaintsAnOpenDrawerBeforeViewportHandshake()
    {
        await Page.SetViewportSizeAsync(390, 812);
        await NavigateToAsync("/");
        await Page.AddInitScriptAsync("""
            window.__aetheusColdLoadViolations = [];
            requestAnimationFrame(function sampleColdLoadFrame() {
                const backdrop = document.querySelector('.sidebar-backdrop');
                if (backdrop) {
                    const style = getComputedStyle(backdrop);
                    const rect = backdrop.getBoundingClientRect();
                    if (style.display !== 'none' && style.visibility !== 'hidden'
                        && rect.width > 0 && rect.height > 0) {
                        window.__aetheusColdLoadViolations.push('visible backdrop');
                    }
                }

                const sidebar = document.querySelector('.rz-sidebar');
                if (sidebar) {
                    const style = getComputedStyle(sidebar);
                    const rect = sidebar.getBoundingClientRect();
                    if (!sidebar.classList.contains('rz-sidebar-collapsed')
                        && style.visibility !== 'hidden' && rect.width > 1) {
                        window.__aetheusColdLoadViolations.push('open drawer');
                    }
                }

                if (!document.querySelector('[data-viewport="ready"]')) {
                    requestAnimationFrame(sampleColdLoadFrame);
                }
            });
            """);

        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.WaitForSelectorAsync("[data-viewport='ready']", new()
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });
        var violations = await Page.EvaluateAsync<string[]>(
            "() => window.__aetheusColdLoadViolations ?? []");

        Assert.That(violations, Is.Empty,
            "The splash/viewport handshake must prevent an open mobile drawer or backdrop from painting.");
    }

    [Test]
    public async Task MobileBackdrop_ClosesDrawerFromARealPointerClick()
    {
        await Page.SetViewportSizeAsync(390, 812);
        await NavigateToAsync("/");
        await Page.Locator("[aria-label='Toggle sidebar']").ClickAsync();
        await Expect(Page.Locator(".sidebar-backdrop")).ToBeVisibleAsync();

        const int exposedBackdropX = 350;
        const int exposedBackdropY = 400;
        var targetClass = await Page.EvaluateAsync<string>(
            "() => document.elementFromPoint(350, 400)?.getAttribute('class') ?? ''");
        Assert.That(targetClass, Does.Contain("sidebar-backdrop"));

        await Page.Mouse.ClickAsync(exposedBackdropX, exposedBackdropY);

        await Expect(Page.Locator(".sidebar-backdrop")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task MobileCurrentRouteLink_ClosesDrawer()
    {
        await Page.SetViewportSizeAsync(390, 812);
        await NavigateToAsync("/");
        await Page.Locator("[aria-label='Toggle sidebar']").ClickAsync();
        await Expect(Page.Locator(".sidebar-backdrop")).ToBeVisibleAsync();

        await SidebarNavItem("Dashboard").ClickAsync();

        await Expect(Page.Locator(".sidebar-backdrop")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task RepresentativePages_RemainReadableInPhoneLandscape()
        => await AuditMobilePathsAsync(["/", "/servers", "/projects"], 812, 390);

    [Test]
    public async Task UnauthenticatedLogin_RemainsReadableInPortraitAndLandscape()
    {
        await NavigateToAsync("/");
        await Page.EvaluateAsync("() => localStorage.clear()");

        foreach (var viewport in new[] { (Width: 390, Height: 812), (Width: 812, Height: 390) })
        {
            await Page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await Page.GotoAsync($"{FrontendUrl}/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(Page.Locator("input[name='Username']")).ToBeVisibleAsync();
            var widths = await Page.EvaluateAsync<int[]>(
                "() => [document.documentElement.scrollWidth, window.innerWidth]");
            Assert.That(widths[0], Is.LessThanOrEqualTo(widths[1]),
                $"The unauthenticated login must fit {viewport.Width}x{viewport.Height}.");
        }
    }

    private async Task AuditMobilePathsAsync(IEnumerable<string> paths, int viewportWidth, int viewportHeight = 812)
    {
        var failures = new List<string>();
        var pendingApiRequests = new HashSet<IRequest>();
        var networkFailures = new List<string>();
        var networkGate = new object();
        void TrackRequest(object? _, IRequest request)
        {
            if (!request.Url.StartsWith(BackendUrl, StringComparison.OrdinalIgnoreCase)) return;
            lock (networkGate) pendingApiRequests.Add(request);
        }
        void FinishRequest(object? _, IRequest request)
        {
            lock (networkGate) pendingApiRequests.Remove(request);
        }
        void FailRequest(object? _, IRequest request)
        {
            lock (networkGate)
            {
                pendingApiRequests.Remove(request);
                networkFailures.Add($"{request.Method} {request.Url}: {request.Failure}");
            }
        }
        void TrackResponse(object? _, IResponse response)
        {
            if (response.Status < 400 || !response.Url.StartsWith(BackendUrl, StringComparison.OrdinalIgnoreCase)) return;
            lock (networkGate) networkFailures.Add($"HTTP {response.Status} {response.Request.Method} {response.Url}");
        }

        Page.Request += TrackRequest;
        Page.RequestFinished += FinishRequest;
        Page.RequestFailed += FailRequest;
        Page.Response += TrackResponse;
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    await AuditMobilePathAsync(path, viewportWidth, viewportHeight);
                }
                catch (Exception exception) when (exception is AssertionException or PlaywrightException or TimeoutException)
                {
                    string networkState;
                    lock (networkGate)
                    {
                        var pending = pendingApiRequests.Select(request => $"pending {request.Method} {request.Url}");
                        networkState = string.Join("; ", networkFailures.Concat(pending));
                        networkFailures.Clear();
                    }
                    failures.Add($"{viewportWidth}x{viewportHeight} {path}: {exception.Message}"
                        + (string.IsNullOrEmpty(networkState) ? string.Empty : $" Network: {networkState}"));
                    var screenshotDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mobile-audit-failures");
                    Directory.CreateDirectory(screenshotDirectory);
                    var safeName = path == "/" ? "dashboard" : string.Concat(path.Trim('/').Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
                    await Page.ScreenshotAsync(new()
                    {
                        Path = Path.Combine(screenshotDirectory, $"{viewportWidth}x{viewportHeight}-{safeName}.png"),
                        FullPage = false
                    });

                    try
                    {
                        // A failed route can leave the browser on an error or login page. Restore a known
                        // authenticated desktop state so one defect does not cascade into every later route.
                        await Page.SetViewportSizeAsync(1280, 720);
                        await LoginAsync(force: true);
                    }
                    catch (Exception recoveryException) when (
                        recoveryException is InvalidOperationException or PlaywrightException or TimeoutException)
                    {
                        failures.Add($"{viewportWidth}px recovery after {path}: {recoveryException.Message}");
                        break;
                    }
                }
            }
        }
        finally
        {
            Page.Request -= TrackRequest;
            Page.RequestFinished -= FinishRequest;
            Page.RequestFailed -= FailRequest;
            Page.Response -= TrackResponse;
        }

        Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures));
    }

    private async Task AuditMobilePathAsync(string path, int viewportWidth, int viewportHeight)
    {
        await Page.SetViewportSizeAsync(viewportWidth, viewportHeight);
        await NavigateToAsync(path);
        await WaitForNoSpinnerAsync();
        await Page.WaitForSelectorAsync(".sidebar-backdrop", new()
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5000
        });
        await Page.WaitForFunctionAsync("""
            () => [...document.querySelectorAll('.rz-data-grid-loading, .rz-datatable-loading')]
                .every(element => {
                    const style = getComputedStyle(element);
                    return style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0';
                })
            """, null, new() { Timeout = 30000 });

        var collapsedSidebarState = await Page.Locator(".rz-sidebar").EvaluateAsync<string[]>("""
            element => {
                const style = getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return [element.className, `${rect.width}`, style.overflowX];
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(collapsedSidebarState[0], Does.Contain("rz-sidebar-collapsed"),
                $"{path} must collapse the mobile drawer after a viewport change.");
            Assert.That(double.Parse(collapsedSidebarState[1], System.Globalization.CultureInfo.InvariantCulture),
                Is.LessThanOrEqualTo(1), $"{path} must leave no visible drawer width.");
            Assert.That(collapsedSidebarState[2], Is.EqualTo("hidden"),
                $"{path} must clip the collapsed drawer contents.");
        });

        var widths = await Page.EvaluateAsync<int[]>("""
            () => [document.documentElement.scrollWidth, window.innerWidth]
            """);

        Assert.That(widths[0], Is.LessThanOrEqualTo(widths[1]),
            $"{path} must keep horizontal scrolling inside tabs, badges or data grids.");

        var clippedInteractiveElements = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('a, button, input, select, textarea, [role="button"]')]
                .filter(element => {
                    const style = getComputedStyle(element);
                    if (style.display === 'none' || style.visibility === 'hidden') return false;
                    const rect = element.getBoundingClientRect();
                    if (rect.width === 0 || rect.height === 0) return false;
                    if (rect.left >= 0 && rect.right <= window.innerWidth) return false;
                    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
                        const overflow = getComputedStyle(parent).overflowX;
                        if (overflow === 'auto' || overflow === 'scroll') return false;
                    }
                    return true;
                })
                .map(element => `${element.tagName.toLowerCase()}[${element.getAttribute('aria-label') ?? element.textContent?.trim() ?? ''}]`)
            """);

        Assert.That(clippedInteractiveElements, Is.Empty,
            $"{path} must not place interactive controls outside the mobile viewport.");

        var obscuredHeaderControls = await Page.EvaluateAsync<string[]>("""
            () => {
                const modalIsVisible = [...document.querySelectorAll('.rz-dialog-wrapper, .rz-dialog-mask')]
                    .some(element => {
                        const style = getComputedStyle(element);
                        const rect = element.getBoundingClientRect();
                        return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                    });
                if (modalIsVisible) return [];

                return ['.app-home-link', '.header-user-btn'].flatMap(selector => {
                    const elements = [...document.querySelectorAll(selector)];
                    return elements.length === 0 ? [{ missingSelector: selector }] : elements;
                })
                .filter(element => {
                    if (element.missingSelector) return true;
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    if (style.display === 'none' || style.visibility === 'hidden' || rect.width === 0 || rect.height === 0)
                        return true;
                    const top = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
                    return top === null || !(element === top || element.contains(top));
                })
                .map(element => {
                    if (element.missingSelector) return `${element.missingSelector} missing`;
                    const rect = element.getBoundingClientRect();
                    const top = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
                    return `${element.className} covered by ${top?.className ?? top?.tagName ?? 'nothing'}`;
                });
            }
            """);

        Assert.That(obscuredHeaderControls, Is.Empty,
            $"{path} must keep the home and user controls visible above page content.");

        var clippedEmptyStateText = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.empty-state')]
                .flatMap(state => [...state.children])
                .filter(element => {
                    if (element.classList.contains('empty-state-icon') ||
                        element.classList.contains('empty-state-actions') ||
                        !element.textContent?.trim()) return false;
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' &&
                        (rect.left < 0 || rect.right > window.innerWidth || element.scrollWidth > element.clientWidth + 1);
                })
                .map(element => element.textContent?.trim() ?? '')
            """);

        Assert.That(clippedEmptyStateText, Is.Empty,
            $"{path} must keep empty-state titles and descriptions readable inside the viewport.");

        var errorCardCount = await Page.Locator(".error-card").CountAsync();
        var notFoundCount = await Page.Locator(".not-found-container").CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(errorCardCount, Is.Zero,
                $"{path} must not render the application error boundary.");
            Assert.That(notFoundCount, Is.Zero,
                $"{path} must resolve to a real page in the demo dataset.");
        });

    }

    [Test]
    public async Task MobileSettingsTabs_ScrollInsideTheTabStrip()
    {
        await NavigateToAsync("/admin/settings");
        await Page.SetViewportSizeAsync(375, 812);
        var tabs = Page.Locator(".url-synced-tabs > .rz-tabview-nav");
        await Expect(tabs).ToBeVisibleAsync();

        var overflowX = await tabs.EvaluateAsync<string>("element => getComputedStyle(element).overflowX");
        Assert.That(overflowX, Is.EqualTo("auto"));
    }

    [Test]
    [Category("Projects")]
    public async Task MobileProjectSettings_UseTheAvailableWidth()
    {
        await NavigateToAsync("/projects/3/edit");
        await Page.SetViewportSizeAsync(375, 812);

        var settings = Page.Locator(".project-ci-settings-row .rz-form-field");
        await Expect(settings).ToHaveCountAsync(3);
        var settingWidths = await settings.EvaluateAllAsync<double[]>(
            "elements => elements.map(element => element.getBoundingClientRect().width)");
        Assert.That(settingWidths, Has.Length.EqualTo(3));
        Assert.That(settingWidths, Has.All.GreaterThanOrEqualTo(280));
    }
}
