// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.PackageFeeds;

/// <summary>
/// Resolves latest package versions against real registries (NuGet, npm, PyPI) over the SSRF-guarded
/// "package-feeds" HttpClient. Maven / Docker / Generic have no resolver yet and return Unsupported.
/// </summary>
public sealed class PackageVersionResolver(IHttpClientFactory httpClientFactory, ILogger<PackageVersionResolver> logger)
    : IPackageVersionResolver
{
    public async Task<PackageResolveResult> ResolveLatestAsync(
        PackageFeedType feedType, string upstreamUrl, string packageName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);

        try
        {
            return feedType switch
            {
                PackageFeedType.NuGet => await ResolveNuGetAsync(upstreamUrl, packageName, ct).ConfigureAwait(false),
                PackageFeedType.Npm => await ResolveNpmAsync(upstreamUrl, packageName, ct).ConfigureAwait(false),
                PackageFeedType.PyPI => await ResolvePyPiAsync(upstreamUrl, packageName, ct).ConfigureAwait(false),
                _ => new PackageResolveResult(PackageResolveOutcome.Unsupported, null, null)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            logger.LogWarning(ex, "Package resolve failed for {Feed} {Package}", feedType, packageName);
            return new PackageResolveResult(PackageResolveOutcome.Error, null, null);
        }
    }

    private HttpClient Client() => httpClientFactory.CreateClient("package-feeds");

    private async Task<PackageResolveResult> ResolveNuGetAsync(string upstreamUrl, string name, CancellationToken ct)
    {
        // Flat container: {base}/v3-flatcontainer/{id-lower}/index.json -> { "versions": [ ... ] } ascending, prereleases included.
        var origin = ResolveOrigin(upstreamUrl) ?? PackageRegistryDefaults.NuGet;
        var id = name.ToLowerInvariant();
        using var response = await Client().GetAsync($"{origin}/v3-flatcontainer/{Uri.EscapeDataString(id)}/index.json", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("versions", out var versions) || versions.GetArrayLength() == 0)
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);

        // Prefer the last STABLE version (no SemVer prerelease '-' suffix); fall back to the last
        // overall only if every listed version is a prerelease, so we never surface a pre as "latest stable".
        string? latest = null;
        foreach (var v in versions.EnumerateArray())
        {
            var s = v.GetString();
            if (!string.IsNullOrEmpty(s) && !s.Contains('-', StringComparison.Ordinal)) latest = s;
        }
        latest ??= versions[versions.GetArrayLength() - 1].GetString();
        return new PackageResolveResult(PackageResolveOutcome.Resolved, latest, null);
    }

    private async Task<PackageResolveResult> ResolveNpmAsync(string upstreamUrl, string name, CancellationToken ct)
    {
        // {base}/{name} -> { "dist-tags": { "latest": v }, "time": { v: iso } }.
        var origin = ResolveOrigin(upstreamUrl) ?? PackageRegistryDefaults.Npm;
        using var response = await Client().GetAsync($"{origin}/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        if (!root.TryGetProperty("dist-tags", out var tags) || !tags.TryGetProperty("latest", out var latestEl))
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);

        var latest = latestEl.GetString();
        DateTime? published = null;
        if (latest is not null && root.TryGetProperty("time", out var time)
            && time.TryGetProperty(latest, out var t) && t.TryGetDateTime(out var dt))
            published = dt.ToUniversalTime();

        return new PackageResolveResult(PackageResolveOutcome.Resolved, latest, published);
    }

    private async Task<PackageResolveResult> ResolvePyPiAsync(string upstreamUrl, string name, CancellationToken ct)
    {
        // {base}/pypi/{name}/json -> { "info": { "version": v }, "releases": { v: [ { "upload_time_iso_8601" } ] } }.
        var origin = ResolveOrigin(upstreamUrl) ?? PackageRegistryDefaults.PyPi;
        using var response = await Client().GetAsync($"{origin}/pypi/{Uri.EscapeDataString(name)}/json", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        if (!root.TryGetProperty("info", out var info) || !info.TryGetProperty("version", out var vEl))
            return new PackageResolveResult(PackageResolveOutcome.NotFound, null, null);

        var latest = vEl.GetString();
        DateTime? published = null;
        if (latest is not null && root.TryGetProperty("releases", out var releases)
            && releases.TryGetProperty(latest, out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0
            && files[0].TryGetProperty("upload_time_iso_8601", out var up) && up.TryGetDateTime(out var dt))
            published = dt.ToUniversalTime();

        return new PackageResolveResult(PackageResolveOutcome.Resolved, latest, published);
    }

    private static string? ResolveOrigin(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
