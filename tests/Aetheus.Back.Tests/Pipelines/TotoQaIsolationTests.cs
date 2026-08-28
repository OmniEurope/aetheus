// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Pipelines;

public sealed class TotoQaIsolationTests
{
    [Fact]
    public void QaDefinition_UsesRunScopedPortForComposeAndDast()
    {
        var root = FindRepoRoot();
        var yaml = File.ReadAllText(Path.Combine(
            root, "deploy", "pipelines", "toto-qa.yaml"));
        var compose = File.ReadAllText(Path.Combine(
            root, "deploy", "pipelines", "toto-e2e-fixture", "docker-compose.qa.yml"));
        var conformanceCompose = File.ReadAllText(Path.Combine(
            root,
            "deploy",
            "pipelines",
            "toto-conformance-fixture",
            "common",
            "deploy",
            "docker-compose.yml.template"));
        var qaBridgeCompose = File.ReadAllText(Path.Combine(
            root,
            "deploy",
            "pipelines",
            "toto-conformance-fixture",
            "common",
            "deploy",
            "docker-compose.qa-bridge.yml.template"));

        Assert.Contains("TOTO_QA_PORT: \"$(BUILD_RUN_PORT)\"", yaml, StringComparison.Ordinal);
        Assert.Contains(
            "TOTO_DAST_TARGET_URL: \"http://127.0.0.1:$(TOTO_QA_PORT)\"",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "${TOTO_PORT:?TOTO_PORT must be assigned per pipeline run}",
            compose,
            StringComparison.Ordinal);
        Assert.DoesNotContain("8091", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("10091", yaml, StringComparison.Ordinal);
        Assert.Contains(
            "docker network inspect bridge",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "export TOTO_BIND_ADDRESS=\"$(cat .qa-bind-address)\"",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "127.0.0.1:${TOTO_PORT}:8080",
            conformanceCompose,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TOTO_BIND_ADDRESS",
            conformanceCompose,
            StringComparison.Ordinal);
        Assert.Contains(
            "${TOTO_BIND_ADDRESS:?TOTO_BIND_ADDRESS must be the private Docker bridge gateway}:${TOTO_PORT}:8080",
            qaBridgeCompose,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "0.0.0.0:${TOTO_PORT}:8080",
            conformanceCompose + qaBridgeCompose,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
