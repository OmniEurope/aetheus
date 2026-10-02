// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class SelfMonitoringDeploymentAuditTests
{
    [Fact]
    public void SelfDeploy_WiresTelemetryAndServerSideWebAnalytics()
    {
        var root = RepoRoot();
        var backProgram = Read(root, "src", "Aetheus.Back", "Program.cs");
        var backProject = Read(root, "src", "Aetheus.Back", "Aetheus.Back.csproj");
        var compose = Read(root, "deploy", "compose", "remote-bluegreen.compose.yml");
        var frontDockerfile = Read(root, "deploy", "docker", "Dockerfile.front");
        var staticServerProject = Read(root, "deploy", "docker", "StaticServer.csproj");
        var staticServer = Read(root, "deploy", "docker", "StaticServer.Program.cs");
        var index = Read(root, "src", "Aetheus.Front", "wwwroot", "index.html");

        Assert.Contains("AddAetheusTelemetry(builder.Configuration", backProgram, StringComparison.Ordinal);
        Assert.Contains("Aetheus.Telemetry.csproj", backProject, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_TELEMETRY_ENABLED=${AETHEUS_TELEMETRY_ENABLED:-false}", compose, StringComparison.Ordinal);
        Assert.Contains("OTEL_EXPORTER_OTLP_HEADERS=${OTEL_EXPORTER_OTLP_HEADERS:-}", compose, StringComparison.Ordinal);
        Assert.Contains("Aetheus.WebAnalytics.csproj", staticServerProject, StringComparison.Ordinal);
        // The static server is published on the host now, so the front image no longer carries the
        // two build stages that had to copy the repository build policy. What the image must still
        // prove is that the server it serves the site with is the one carrying the analytics
        // endpoint, which is the COPY below plus the project reference asserted above.
        Assert.Contains("COPY .pipeline-publish/static-server/ .", frontDockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", frontDockerfile, StringComparison.Ordinal);
        Assert.Contains("AddAetheusWebAnalytics(builder.Configuration, options => options.AcceptDeclaredUserId = true)", staticServer, StringComparison.Ordinal);
        // R-471: behind Apache the visitor address is the forwarded one, not the proxy.
        Assert.Contains("app.UseForwardedHeaders(forwardedHeadersOptions);", staticServer, StringComparison.Ordinal);
        Assert.Contains("MapAetheusWebAnalytics()", staticServer, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY=${AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY:-}", compose, StringComparison.Ordinal);
        Assert.Contains("aetheus-analytics-bootstrap.js", index, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserBootstrap_ReplacesUnknownRouteValuesBeforeExport()
    {
        var bootstrap = Read(
            RepoRoot(), "src", "Aetheus.Front", "wwwroot", "js", "aetheus-analytics-bootstrap.js");

        Assert.Contains("return \"{value}\"", bootstrap, StringComparison.Ordinal);
        Assert.Contains("routeResolver: resolveAetheusAnalyticsRoute", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", bootstrap, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
