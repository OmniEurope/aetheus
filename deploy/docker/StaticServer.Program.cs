// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.ResponseCompression;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "appsettings.json");
if (File.Exists(settingsPath))
{
    var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
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

var app = builder.Build();

app.UseResponseCompression();

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
app.MapFallbackToFile("index.html");

app.Run();
