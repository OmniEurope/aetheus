// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace Aetheus.E2E;

/// <summary>
/// PLAN-008 lot 0: the behaviour and rendering reference of every routed page, taken before the
/// OmniEurope.Blazor migration. For each <c>@page</c> route it writes four screenshots
/// (1440x900 and 375x812, dark and light) and a DOM inventory per viewport (accessible names of
/// actions, field labels, column headers, tabs, headings). Values are left out of the inventory and
/// digits are normalised, so two runs on the same seed produce the same inventories and the
/// post-migration run can be diffed against this one.
/// The fixture is explicit: it only runs when selected with <c>launch-windows.ps1 -tec Parity</c>.
/// Output goes outside the repository: <c>AETHEUS_PARITY_DIR</c> (default <c>C:\Dev\Aetheus.parity</c>)
/// / <c>AETHEUS_PARITY_LABEL</c> (default <c>baseline</c>) / short commit / run timestamp.
/// </summary>
[TestFixture]
[Category("Parity")]
[Explicit("PLAN-008 lot 0 reference capture; run with launch-windows.ps1 -tec Parity.")]
[NonParallelizable]
public sealed class ParityBaselineTests : E2ETestBase
{
    private const string FrenchCulture = "fr-FR";
    private const string RazorExtension = "razor";

    // Captures a copy of the interface served under a route prefix, under the same route list, for
    // comparison with a reference taken on the bare routes. The inventory's path drops the prefix,
    // so both runs describe the same page the same way.
    private static readonly string AreaPrefix = Environment.GetEnvironmentVariable("AETHEUS_PARITY_PREFIX") is { Length: > 0 } prefix
        ? "/" + prefix.Trim('/')
        : string.Empty;
    private static readonly Regex RouteDirective = new("^@page \"(?<route>[^\"]+)\"", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex RouteParameter = new(@"\{(?<name>[A-Za-z]+)(?::(?<constraint>[a-z]+))?\}", RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, string> ResolvedByCrawl = new(StringComparer.Ordinal);
    private static readonly List<RouteRecord> Records = [];
    private static readonly Lazy<string> RunDirectory = new(CreateRunDirectory);

    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("desktop", 1440, 900),
        ("mobile", 375, 812)
    ];

    private static readonly string[] Themes = ["dark", "light"];
    private static readonly string[] BlockingAccessibilityImpacts = ["serious", "critical"];
    private static readonly AxeRunOptions AccessibilityScanOptions = new()
    {
        RunOnly = new RunOnlyOptions
        {
            Type = "tag",
            Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]
        }
    };

    public static IEnumerable<TestCaseData> Routes()
    {
        // Static routes first: the parameterised ones are resolved from links seen on them. The
        // zero-padded index keeps that order whatever the runner's name ordering is.
        var requestedRoutes = Environment.GetEnvironmentVariable("AETHEUS_PARITY_ROUTE")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var routes = DiscoverRoutes().AsEnumerable();
        if (requestedRoutes is { Length: > 0 })
            routes = routes.Where(route => requestedRoutes.Contains(route, StringComparer.Ordinal));

        return routes
            .OrderBy(route => route.Contains('{', StringComparison.Ordinal))
            .ThenBy(route => route, StringComparer.Ordinal)
            .Select((route, index) => new TestCaseData(route).SetName($"Capture {index:D3} {route}"));
    }

    [OneTimeTearDown]
    public void WriteIndex()
    {
        if (!RunDirectory.IsValueCreated)
            return;

        var directory = RunDirectory.Value;
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(Records, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "index.html"), BuildIndex(Records));
        TestContext.Progress.WriteLine($"[PARITY] {Records.Count} routes captured in {directory}");
    }

    [TestCaseSource(nameof(Routes))]
    public async Task CaptureRoute(string template)
    {
        var (path, resolution) = Resolve(template);
        var routeDirectory = Path.Combine(RunDirectory.Value, RouteKey(template));
        Directory.CreateDirectory(routeDirectory);

        var record = new RouteRecord(template, path, resolution);
        foreach (var theme in Themes)
        {
            foreach (var (name, width, height) in Viewports)
            {
                var capture = await CaptureAsync(template, path, theme, name, width, height, routeDirectory);
                record.Captures.Add(capture);
            }
        }

        lock (Records)
            Records.Add(record);

        Assert.Multiple(() =>
        {
            Assert.That(record.Captures, Has.Count.EqualTo(Themes.Length * Viewports.Length));
            Assert.That(record.Captures.Where(capture => capture.Failure is not null), Is.Empty,
                () => string.Join(" | ", record.Captures
                    .Where(capture => capture.Failure is not null)
                    .Select(capture => $"{capture.Viewport}-{capture.Theme}: {capture.Failure}")));
            Assert.That(record.Captures.Select(capture => File.Exists(capture.Screenshot)), Has.All.True,
                "every screenshot must be written");
            Assert.That(record.Captures.Where(capture => capture.Inventory is not null)
                    .Select(capture => File.Exists(capture.Inventory!)), Has.All.True,
                "every inventory must be written");
        });
    }

    private async Task<CaptureRecord> CaptureAsync(
        string template, string path, string theme, string viewport, int width, int height, string routeDirectory)
    {
        var anonymous = template == "/login";
        var options = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            Locale = FrenchCulture,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            StorageState = anonymous ? null : WithPreferences(E2EAuthSession.StorageStateJson, theme)
        };

        var context = await Browser.NewContextAsync(options);
        var diagnostics = new List<string>();
        var failures = new List<string>();
        var prefix = $"{viewport}-{theme}";
        var screenshot = Path.Combine(routeDirectory, $"{prefix}.png");
        string? inventoryPath = null;
        var finalPath = path;
        var notFound = false;
        try
        {
            await context.AddInitScriptAsync(
                $"localStorage.setItem('aetheus_theme', '{theme}'); localStorage.setItem('aetheus_lang', '{FrenchCulture}');");
            var page = await context.NewPageAsync();
            page.Console += (_, message) =>
            {
                if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add($"console error: {message.Text}");
            };
            page.PageError += (_, exception) => diagnostics.Add($"page error: {exception}");

            // Every step is bounded: a page that keeps the browser's main thread busy must be recorded as
            // a failed capture, not stall the whole reference run (route 4 did exactly that, unbounded).
            await StepAsync(failures, "load", async () =>
            {
                await page.GotoAsync($"{FrontendUrl}{AreaPath(path)}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.WaitForSelectorAsync(
                    $"[data-testid='{PlaywrightConfig.BlazorReadyTestId}'], .omni-panel-menu, #Username",
                    new() { State = WaitForSelectorState.Attached, Timeout = PlaywrightConfig.AppReadyTimeoutMs });
                await WaitForSettledAsync(page);
                await page.EvaluateAsync(
                    "theme => { localStorage.setItem('aetheus_theme', theme); window.Aetheus.setOmniTheme(theme); }",
                    theme);
                await page.WaitForFunctionAsync(
                    "expected => document.documentElement.getAttribute('data-omni-theme') === expected",
                    theme,
                    new() { Timeout = PlaywrightConfig.AppReadyTimeoutMs });
                await page.WaitForTimeoutAsync(250);
            });
            await StepAsync(failures, "visual integrity", async () =>
            {
                var audit = await page.EvaluateAsync<JsonElement>(VisualIntegrityScript);
                var overflowPixels = audit.GetProperty("documentOverflowPixels").GetInt32();
                var hasBlazorError = audit.GetProperty("hasBlazorError").GetBoolean();
                var suspiciousText = audit.GetProperty("suspiciousText").EnumerateArray()
                    .Select(value => value.GetString())
                    .OfType<string>()
                    .ToList();
                if (overflowPixels > 1)
                    failures.Add($"visual integrity: document overflows horizontally by {overflowPixels}px");
                if (hasBlazorError)
                    failures.Add("visual integrity: a Blazor error boundary is visible");
                if (suspiciousText.Count > 0)
                    failures.Add($"visual integrity: suspicious rendered text: {string.Join(", ", suspiciousText)}");

                var accessibility = await page.RunAxe(AccessibilityScanOptions);
                var blocking = accessibility.Violations
                    .Where(violation => violation.Impact is not null
                        && BlockingAccessibilityImpacts.Contains(violation.Impact, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (blocking.Count > 0)
                {
                    failures.Add("accessibility: " + string.Join("; ", blocking.Select(violation =>
                        $"{violation.Id} ({violation.Impact}, {violation.Nodes.Length} node(s): "
                        + string.Join(", ", violation.Nodes.Take(8).Select(node =>
                            $"{node.Target}: {node.Any.FirstOrDefault()?.Message}")))));
                }
            });
            await StepAsync(failures, "screenshot", () =>
                page.ScreenshotAsync(new() { Path = screenshot, FullPage = true, Timeout = 30_000 }));

            // One inventory per viewport is enough: the theme does not change what the page offers.
            if (theme == Themes[0])
            {
                await StepAsync(failures, "inventory", async () =>
                {
                    var inventory = await page.EvaluateAsync<JsonElement>(InventoryScript, AreaPrefix);
                    inventoryPath = Path.Combine(routeDirectory, $"inventory-{viewport}.json");
                    await File.WriteAllTextAsync(inventoryPath, WithoutReferenceMarkers(inventory));
                    if (viewport == Viewports[0].Name)
                        RememberLinks(inventory);
                });
            }

            finalPath = new Uri(page.Url).AbsolutePath;
            await StepAsync(failures, "not-found probe", async () =>
            {
                notFound = finalPath.Contains("not-found", StringComparison.OrdinalIgnoreCase)
                           || await page.Locator(".not-found-container").CountAsync() > 0;
            });
        }
        finally
        {
            // Closing a context whose page is stuck can itself hang; bound it too.
            try
            {
                await context.CloseAsync().WaitAsync(StepTimeout);
            }
            catch (TimeoutException)
            {
                failures.Add("close: timed out");
            }
        }

        var failure = failures.Count == 0 ? null : string.Join(" | ", failures);
        return new CaptureRecord(viewport, theme, screenshot, inventoryPath, finalPath, notFound, failure, diagnostics);
    }

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private static async Task StepAsync(List<string> failures, string step, Func<Task> action)
    {
        try
        {
            await action().WaitAsync(StepTimeout);
        }
        catch (TimeoutException)
        {
            failures.Add($"{step}: timed out after {StepTimeout.TotalSeconds:0} s");
        }
        catch (PlaywrightException exception)
        {
            failures.Add($"{step}: {exception.Message.Split('\n')[0]}");
        }
    }

    // Playwright's evaluate result carries "$id" reference markers; they are not page content and would
    // differ between runs, so the written inventory keeps only the page's own properties.
    private static string WithoutReferenceMarkers(JsonElement inventory)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in inventory.EnumerateObject().Where(property => !property.Name.StartsWith('$')))
            {
                writer.WritePropertyName(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray();
                    foreach (var item in property.Value.EnumerateArray().Where(item => item.ValueKind != JsonValueKind.Object))
                        item.WriteTo(writer);
                    writer.WriteEndArray();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WaitForSettledAsync(IPage page)
    {
        // Same readiness rule as E2ETestBase: every page-level loader must be gone. A determinate
        // OmniProgressBar is page content (wizard progress, CPU, memory, disk), not a loading signal.
        await page.WaitForFunctionAsync("""
            () => [...document.querySelectorAll('.aetheus-loader-centered')]
                .every(element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display === 'none' || style.visibility === 'hidden' ||
                        rect.width === 0 || rect.height === 0;
                })
            """, null, new() { Timeout = PlaywrightConfig.AppReadyTimeoutMs });
        // Grids and cards fill in a second render after the loaders clear; there is no single DOM
        // signal for "every panel has its data", so give the page a bounded settle time.
        await page.WaitForTimeoutAsync(1000);
    }

    private static void RememberLinks(JsonElement inventory)
    {
        if (!inventory.TryGetProperty("hrefs", out var hrefs))
            return;

        foreach (var template in ParameterisedTemplates.Value)
        {
            if (ResolvedByCrawl.ContainsKey(template.Template))
                continue;
            foreach (var href in hrefs.EnumerateArray().Select(value => value.GetString()).OfType<string>())
            {
                if (template.Pattern.IsMatch(href))
                {
                    ResolvedByCrawl.TryAdd(template.Template, href);
                    break;
                }
            }
        }
    }

    private static readonly Lazy<IReadOnlyList<(string Template, Regex Pattern)>> ParameterisedTemplates = new(() =>
        DiscoverRoutes()
            .Where(route => route.Contains('{', StringComparison.Ordinal))
            .Select(route => (route, new Regex(
                "^" + RouteParameter.Replace(Regex.Escape(route).Replace(@"\{", "{", StringComparison.Ordinal),
                    match => match.Groups["constraint"].Value == "int" ? @"\d+" : "[^/?#]+") + "$",
                RegexOptions.CultureInvariant)))
            .ToList());

    private static string AreaPath(string path) =>
        AreaPrefix.Length == 0 ? path : path == "/" ? AreaPrefix : AreaPrefix + path;

    private static string WithPreferences(string? storageStateJson, string theme)
    {
        var root = JsonNode.Parse(storageStateJson
                                  ?? throw new InvalidOperationException("Authenticated storage state is unavailable."))!
            .AsObject();
        var origins = root["origins"]?.AsArray()
                      ?? throw new InvalidOperationException("Authenticated storage state has no origins.");
        var origin = origins.FirstOrDefault(node =>
            string.Equals(node?["origin"]?.GetValue<string>(),
                new Uri(FrontendUrl).GetLeftPart(UriPartial.Authority), StringComparison.Ordinal));
        var localStorage = origin?["localStorage"]?.AsArray()
                           ?? throw new InvalidOperationException("Authenticated storage state has no localStorage.");

        SetStorageValue(localStorage, "aetheus_theme", theme);
        SetStorageValue(localStorage, "aetheus_lang", FrenchCulture);
        return root.ToJsonString();
    }

    private static void SetStorageValue(JsonArray localStorage, string name, string value)
    {
        var existing = localStorage.FirstOrDefault(node =>
            string.Equals(node?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing["value"] = value;
            return;
        }

        localStorage.Add(new JsonObject
        {
            ["name"] = name,
            ["value"] = value
        });
    }

    private static (string Path, string Resolution) Resolve(string template)
    {
        if (!template.Contains('{', StringComparison.Ordinal))
            return (template, "static");
        if (ResolvedByCrawl.TryGetValue(template, out var crawled))
            return (crawled, "crawl");

        // The E2E database is reset and re-seeded before the run, so the first seeded entity of each
        // kind has id 1. The capture records this as an assumption and flags a not-found result.
        var assumed = RouteParameter.Replace(template, match => match.Groups["constraint"].Value == "int" ? "1" : "default");
        return (assumed, "assumed-id-1");
    }

    private static IReadOnlyList<string> DiscoverRoutes()
    {
        var front = Path.Combine(RepositoryRoot(), "src", "Aetheus.Front");
        return Directory.EnumerateFiles(front, $"*.{RazorExtension}", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => RouteDirective.Matches(File.ReadAllText(file)).Select(match => match.Groups["route"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Aetheus.slnx not found above the test directory.");
    }

    private static string CreateRunDirectory()
    {
        var root = Environment.GetEnvironmentVariable("AETHEUS_PARITY_DIR") is { Length: > 0 } configured
            ? configured
            : @"C:\Dev\Aetheus.parity";
        var label = Environment.GetEnvironmentVariable("AETHEUS_PARITY_LABEL") is { Length: > 0 } configuredLabel
            ? configuredLabel
            : "baseline";
        var directory = Path.Combine(root, label, ShortCommit(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ShortCommit()
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                WorkingDirectory = RepositoryRoot(),
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            var output = git?.StandardOutput.ReadToEnd().Trim();
            git?.WaitForExit();
            return string.IsNullOrWhiteSpace(output) ? "unknown" : output;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    private static string RouteKey(string template)
    {
        var key = Regex.Replace(template.Trim('/'), "[^A-Za-z0-9]+", "_").Trim('_');
        return key.Length == 0 ? "root" : key;
    }

    private static string BuildIndex(IEnumerable<RouteRecord> records)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><meta charset=\"utf-8\"><title>PLAN-008 parity reference</title>")
            .Append("<style>body{font-family:system-ui;margin:1rem}section{margin-bottom:2rem}img{width:23%;margin:.5%;border:1px solid #888;vertical-align:top}mark{color:#b00;background:none}</style>")
            .Append("<h1>PLAN-008 parity reference</h1>");
        foreach (var record in records.OrderBy(record => record.Template, StringComparer.Ordinal))
        {
            html.Append("<section><h2>").Append(WebUtility.HtmlEncode(record.Template)).Append("</h2><p>")
                .Append(WebUtility.HtmlEncode($"{record.Path} ({record.Resolution})"));
            foreach (var capture in record.Captures.Where(capture => capture.NotFound || capture.Failure is not null || capture.Diagnostics.Count > 0))
            {
                html.Append("<br><mark>")
                    .Append(WebUtility.HtmlEncode($"{capture.Viewport}-{capture.Theme}: not-found={capture.NotFound}, failure={capture.Failure}, diagnostics={capture.Diagnostics.Count}"))
                    .Append("</mark>");
            }
            html.Append("</p>");
            foreach (var capture in record.Captures)
            {
                var relative = Path.GetRelativePath(RunDirectory.Value, capture.Screenshot).Replace('\\', '/');
                html.Append("<a href=\"").Append(relative).Append("\"><img loading=\"lazy\" src=\"").Append(relative)
                    .Append("\" alt=\"").Append(WebUtility.HtmlEncode($"{capture.Viewport} {capture.Theme}")).Append("\"></a>");
            }
            html.Append("</section>");
        }
        return html.ToString();
    }

    // Visible elements only, values excluded, digits normalised to '#': the inventory describes what
    // a user can do on the page, not the data of the moment. Names are library independent: icon
    // glyph text (icon-font ligatures), aria-hidden decoration and the text of nested actions are removed
    // before reading an element's own text, and every list is a sorted set, so markup nesting and
    // duplicate wrappers do not show up as differences after the component library changes.
    private const string InventoryScript = """
        prefix => {
            const actionable = 'button, [role="button"], a[href], [role="menuitem"], [role="tab"]';
            const decoration = '.rzi, .material-symbols-outlined, .material-icons, [aria-hidden="true"], svg';
            const norm = text => (text || '').replace(/\s+/g, ' ').replace(/\d+/g, '#').trim();
            const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length)
                && getComputedStyle(element).visibility !== 'hidden';
            const ownText = element => {
                const clone = element.cloneNode(true);
                clone.querySelectorAll(`${decoration}, ${actionable}`).forEach(node => node.remove());
                return clone.textContent || '';
            };
            const nameOf = element => {
                const labelledBy = element.getAttribute('aria-labelledby');
                const labelled = labelledBy
                    ? labelledBy.split(/\s+/).map(id => ownText(document.getElementById(id) || document.createElement('i'))).join(' ')
                    : '';
                return norm(element.getAttribute('aria-label') || labelled || ownText(element) || element.getAttribute('title') || '');
            };
            const labelOf = field => {
                const byFor = field.id ? document.querySelector(`label[for="${CSS.escape(field.id)}"]`) : null;
                const wrapping = field.closest('label');
                return norm(field.getAttribute('aria-label') || (byFor && ownText(byFor)) || (wrapping && ownText(wrapping))
                    || field.getAttribute('placeholder') || field.getAttribute('name') || '');
            };
            // Monaco's own markup is left out: its hidden input gets its label only once the editor has
            // finished loading (two runs of the same commit disagreed on it), and it is the editor's
            // internals, not an action of the page.
            const all = selector => [...document.querySelectorAll(selector)]
                .filter(element => visible(element) && !element.closest('.monaco-editor'));
            const set = values => [...new Set(values.filter(Boolean))].sort();
            const hrefs = set(all('a[href]')
                .map(anchor => { try { return new URL(anchor.href, location.href); } catch { return null; } })
                .filter(url => url && url.origin === location.origin)
                .map(url => url.pathname));
            return {
                path: (prefix && location.pathname.startsWith(prefix) ? location.pathname.slice(prefix.length) || '/' : location.pathname).replace(/\d+/g, '#'),
                headings: set(all('h1, h2, h3, h4, h5, h6, [role="heading"]').map(heading => norm(ownText(heading)))),
                actions: set(all('button, [role="button"], a[href], [role="menuitem"]').map(nameOf)),
                fields: set(all('input:not([type="hidden"]), select, textarea, [role="combobox"], [role="switch"], [role="checkbox"]')
                    .map(field => `${field.getAttribute('type') || field.tagName.toLowerCase()}:${labelOf(field)}`)),
                columns: set(all('th, [role="columnheader"]').map(header => norm(ownText(header)))),
                tabs: set(all('[role="tab"]').map(nameOf)),
                hrefs
            };
        }
        """;

    private const string VisualIntegrityScript = """
        () => {
            const visible = element => {
                const style = getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== 'none' && style.visibility !== 'hidden'
                    && rect.width > 0 && rect.height > 0;
            };
            const suspiciousValues = new Set(['NaN', '[object Object]', 'undefined']);
            const suspicious = [...document.querySelectorAll('body *')]
                .filter(element => element.children.length === 0 && visible(element))
                .map(element => (element.textContent || '').trim())
                .filter(value => suspiciousValues.has(value))
                .filter((value, index, values) => values.indexOf(value) === index);
            return {
                documentOverflowPixels: Math.max(0,
                    document.documentElement.scrollWidth - document.documentElement.clientWidth),
                hasBlazorError: [...document.querySelectorAll('.blazor-error-boundary, #blazor-error-ui')]
                    .some(visible),
                suspiciousText: suspicious
            };
        }
        """;

    private sealed record RouteRecord(string Template, string Path, string Resolution)
    {
        public List<CaptureRecord> Captures { get; } = [];
    }

    private sealed record CaptureRecord(
        string Viewport,
        string Theme,
        string Screenshot,
        string? Inventory,
        string FinalPath,
        bool NotFound,
        string? Failure,
        IReadOnlyList<string> Diagnostics);

}
