// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class FrontendCachePolicyAuditTests
{
    [Fact]
    public void StaticServer_PreventsDeploymentBoundHtmlShellFromBeingCached()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot(), "deploy", "docker", "StaticServer.Program.cs"));

        Assert.Contains("text/html", program, StringComparison.Ordinal);
        Assert.Contains("/_content/Radzen.Blazor", program, StringComparison.Ordinal);
        Assert.Contains("no-store, no-cache, must-revalidate", program, StringComparison.Ordinal);
        Assert.Contains("Headers.Pragma = \"no-cache\"", program, StringComparison.Ordinal);
        Assert.Contains("Headers.Expires = \"0\"", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("app.Use(async", StringComparison.Ordinal)
            < program.IndexOf("app.UseBlazorFrameworkFiles()", StringComparison.Ordinal),
            "The cache policy must run before static and fallback responses are produced.");
    }

    [Fact]
    public void StaticServer_EmitsSecurityHeadersWithoutDependingOnApache()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot(), "deploy", "docker", "StaticServer.Program.cs"));

        Assert.Contains("Headers[\"X-Content-Type-Options\"] = \"nosniff\"", program, StringComparison.Ordinal);
        Assert.Contains("Headers[\"X-Frame-Options\"] = \"DENY\"", program, StringComparison.Ordinal);
        Assert.Contains("Headers[\"Content-Security-Policy\"] = contentSecurityPolicy", program, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", program, StringComparison.Ordinal);
        Assert.Contains("base-uri 'self'", program, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("Headers[\"X-Content-Type-Options\"]", StringComparison.Ordinal)
            < program.IndexOf("app.UseBlazorFrameworkFiles()", StringComparison.Ordinal),
            "Security headers must be registered before static and fallback responses are produced.");
    }

    [Fact]
    public void StaticServer_AuthorizesPublishedInlineScriptsByHashWithoutUnsafeInline()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot(), "deploy", "docker", "StaticServer.Program.cs"));

        Assert.Contains("Regex.Matches", program, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", program, StringComparison.Ordinal);
        Assert.Contains("'sha256-{hash}'", program, StringComparison.Ordinal);
        Assert.Contains("inlineScriptHashes.Distinct()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_Csp_AllowsOnlyDynamicStyleElementsAndAttributes()
    {
        var root = RepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "deploy", "docker", "StaticServer.Program.cs"));
        var apache = File.ReadAllText(Path.Combine(root, "deploy", "apache", "aetheus.conf"));
        var index = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "index.html"));

        Assert.DoesNotContain("style-src 'self' 'unsafe-inline'", program, StringComparison.Ordinal);
        Assert.DoesNotContain("style-src 'self' 'unsafe-inline'", apache, StringComparison.Ordinal);
        Assert.Contains("style-src-elem 'self' 'unsafe-inline'", program, StringComparison.Ordinal);
        Assert.Contains("style-src-elem 'self' 'unsafe-inline'", apache, StringComparison.Ordinal);
        Assert.Contains("style-src-attr 'unsafe-inline'", program, StringComparison.Ordinal);
        Assert.Contains("style-src-attr 'unsafe-inline'", apache, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RadzenAssets_AreCacheBustedWithTheReferencedPackageVersion()
    {
        var root = RepoRoot();
        var packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            packages,
            "<PackageVersion Include=\"Radzen\\.Blazor\" Version=\"(?<version>[^\"]+)\" />");
        Assert.True(versionMatch.Success, "The centrally managed Radzen.Blazor version was not found.");

        var version = versionMatch.Groups["version"].Value;
        var index = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "index.html"));
        var urls = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "Services", "RadzenAssetUrls.cs"));

        Assert.Contains($"Radzen.Blazor.js?v={version}", index, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            index,
            $"Radzen\\.Blazor/css/material-(?:dark-)?base\\.css\\?v={System.Text.RegularExpressions.Regex.Escape(version)}").Count);
        Assert.Contains($"Version = \"{version}\"", urls, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
