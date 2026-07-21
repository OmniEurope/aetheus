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
