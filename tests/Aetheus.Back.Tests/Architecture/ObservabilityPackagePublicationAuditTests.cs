// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class ObservabilityPackagePublicationAuditTests
{
    private static string Root => FindRepoRoot();

    [Fact]
    public void ConsumerManifestPinsVersionsAndDoesNotContainCredentials()
    {
        var manifest = File.ReadAllText(Path.Combine(
            Root, "examples", "optional-observability", "aetheus.integrations.json"));
        Assert.Contains("\"Aetheus.Telemetry\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"Aetheus.WebAnalytics\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.1.0\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("token", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", manifest, StringComparison.OrdinalIgnoreCase);

        var resolver = File.ReadAllText(Path.Combine(
            Root, "deploy", "scripts", "resolve-injected-nuget-packages.mjs"));
        Assert.Contains("Digest mismatch", resolver, StringComparison.Ordinal);
        Assert.Contains("<trustedSigners>", resolver, StringComparison.Ordinal);
        Assert.Contains("join(verificationDirectory, \"NuGet.config\")", resolver, StringComparison.Ordinal);
        Assert.Contains("allowUntrustedRoot=\"true\"", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("repository name=\\\"nuget.org\\\"", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("spawnSync", resolver, StringComparison.Ordinal);

        var consumerPipeline = File.ReadAllText(Path.Combine(
            Root, "deploy", "pipelines", "toto-ci.yaml"));
        Assert.Contains("dotnet nuget verify --all", consumerPipeline, StringComparison.Ordinal);
        Assert.Contains("cd .aetheus-injection/verify", consumerPipeline, StringComparison.Ordinal);
        Assert.Contains("WarningsNotAsErrors=NU3018%3BNU3027%3BNU3042", consumerPipeline, StringComparison.Ordinal);
        Assert.Contains("signature-verification.txt", consumerPipeline, StringComparison.Ordinal);

        var totoDockerfile = File.ReadAllText(Path.Combine(
            Root, "deploy", "pipelines", "toto-conformance-fixture", "common", "deploy", "Dockerfile.template"));
        Assert.Contains("dotnet nuget verify --all", totoDockerfile, StringComparison.Ordinal);
        Assert.Contains("WarningsNotAsErrors=NU3018%3BNU3027%3BNU3042", totoDockerfile, StringComparison.Ordinal);

        var totoCompose = File.ReadAllText(Path.Combine(
            Root, "deploy", "pipelines", "toto-conformance-fixture", "common", "deploy", "docker-compose.yml.template"));
        Assert.Contains(
            "AETHEUS_TELEMETRY_APPLICATION_ID: \"${AETHEUS_TELEMETRY_APPLICATION_ID:-0}\"",
            totoCompose,
            StringComparison.Ordinal);
        Assert.Contains(
            "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT: \"${OTEL_EXPORTER_OTLP_METRICS_ENDPOINT:-}\"",
            totoCompose,
            StringComparison.Ordinal);
        Assert.Contains(
            "AETHEUS_WEB_ANALYTICS_APPLICATION_ID: \"${AETHEUS_WEB_ANALYTICS_APPLICATION_ID:-0}\"",
            totoCompose,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AETHEUS_MONITORED_APPLICATION_ID", totoCompose, StringComparison.Ordinal);
        Assert.Contains("signer-fingerprint.txt", totoDockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("codesignctl.pem", totoDockerfile, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
