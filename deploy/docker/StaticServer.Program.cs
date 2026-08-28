// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aetheus.WebAnalytics;
using Microsoft.AspNetCore.ResponseCompression;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "appsettings.json");
var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
if (File.Exists(settingsPath))
{
    var appVersion = Environment.GetEnvironmentVariable("APP_VERSION");
    if (!string.IsNullOrWhiteSpace(apiBaseUrl) || !string.IsNullOrWhiteSpace(appVersion))
    {
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))?.AsObject()
            ?? throw new InvalidOperationException("Frontend appsettings.json must contain a JSON object.");
        if (!string.IsNullOrWhiteSpace(apiBaseUrl)) settings["ApiBaseUrl"] = apiBaseUrl;
        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            var appSettings = settings["App"] as JsonObject ?? new JsonObject();
            settings["App"] = appSettings;
            appSettings["Version"] = appVersion;
        }

        var temporaryPath = settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, settingsPath, overwrite: true);
        File.Delete(settingsPath + ".br");
        File.Delete(settingsPath + ".gz");
    }
}

var builder = WebApplication.CreateBuilder(args);

// The Blazor WASM bundle (.wasm/.dll/.json/.js) is highly compressible and was
// served UNCOMPRESSED - the dominant cause of slow first paint in production.
// Brotli (gzip fallback) cuts the payload ~4-6x. Static file middleware emits
// ETag/Last-Modified so repeat loads are 304s (no recompression); responses that
// are already content-encoded are passed through untouched (no double work).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/wasm",
        "application/octet-stream",
        "application/manifest+json",
        "image/svg+xml"
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
builder.Services.AddAetheusWebAnalytics(builder.Configuration);

var app = builder.Build();

app.UseResponseCompression();

var cspConnectSources = new List<string> { "'self'" };
if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri))
{
    cspConnectSources.Add(apiUri.GetLeftPart(UriPartial.Authority));
    var webSocketScheme = apiUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
    cspConnectSources.Add($"{webSocketScheme}://{apiUri.Authority}");
}

// Blazor's published index contains a generated inline import map. The shell also has two
// deliberately inline boot snippets that must run before/after the framework loader. Keep
// script-src strict by authorizing only the exact inline content shipped in this image.
// HTML parsing normalizes CRLF/CR to LF before CSP hashes are evaluated, so hash that form.
var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
var inlineScriptHashes = new List<string>();
if (File.Exists(indexPath))
{
    var indexHtml = await File.ReadAllTextAsync(indexPath);
    foreach (Match match in Regex.Matches(
                 indexHtml,
                 "<script\\b(?![^>]*\\bsrc\\s*=)[^>]*>(?<content>.*?)</script>",
                 RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
    {
        var normalizedContent = match.Groups["content"].Value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent)));
        inlineScriptHashes.Add($"'sha256-{hash}'");
    }
}

var contentSecurityPolicy =
    "default-src 'self'; " +
    $"script-src 'self' 'wasm-unsafe-eval' blob: {string.Join(' ', inlineScriptHashes.Distinct())}; " +
    "worker-src 'self' blob:; " +
    "style-src 'self'; " +
    "style-src-elem 'self' 'unsafe-inline'; " +
    "style-src-attr 'unsafe-inline'; " +
    "img-src 'self' data: blob:; " +
    "font-src 'self' data:; " +
    $"connect-src {string.Join(' ', cspConnectSources.Distinct())}; " +
    "frame-ancestors 'none'; " +
    "base-uri 'self'; " +
    "form-action 'self'";

// QA scans this container directly, before an Apache reverse proxy is involved. Emit the same
// browser protections here so static assets, fallback HTML and error responses are all covered.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
    await next();
});

// The published index contains the generated import map for the exact Blazor
// runtime files shipped by this image. Reusing it after a blue/green switch can
// therefore point an existing browser at framework hashes that only existed in
// the previous container. The HTML shell must never outlive the deployment that
// produced it. Framework/static resources remain revalidatable through their
// ETag/Last-Modified headers, so repeat visits still use cheap 304 responses.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var isHtml = context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
        var isVersionCoupledRadzenAsset = context.Request.Path.StartsWithSegments(
            "/_content/Radzen.Blazor", StringComparison.OrdinalIgnoreCase);
        if (isHtml || isVersionCoupledRadzenAsset)
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapGet("/aetheus-analytics/v1/configuration", (AetheusWebAnalyticsOptions options) =>
    Results.Ok(new { enabled = options.Enabled }));
app.MapAetheusWebAnalytics();
app.MapFallbackToFile("index.html");

app.Run();
