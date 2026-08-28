// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class DockerBuildContextAuditTests
{
    [Fact]
    public void RootDockerIgnore_ExcludesHostDotnetBuildOutputs()
    {
        var root = FindRepoRoot();
        var rules = File.ReadAllLines(Path.Combine(root, ".dockerignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        Assert.Contains("**/bin", rules);
        Assert.Contains("**/obj", rules);
        Assert.DoesNotContain(rules, rule =>
            rule.StartsWith('!')
            && (rule.Contains("/bin", StringComparison.OrdinalIgnoreCase)
                || rule.Contains("/obj", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BackDockerfile_RestoresRidAssetsBeforeCopyingSourceTrees()
    {
        var root = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.back"));

        var restore = dockerfile.IndexOf(
            "dotnet restore Aetheus.Back/Aetheus.Back.csproj -r linux-x64",
            StringComparison.Ordinal);
        var sourceCopy = dockerfile.IndexOf("COPY src/Aetheus.Back/ Aetheus.Back/", StringComparison.Ordinal);
        var publish = dockerfile.IndexOf(
            "dotnet publish Aetheus.Back/Aetheus.Back.csproj",
            StringComparison.Ordinal);

        Assert.True(restore >= 0, "The backend image must restore linux-x64 assets explicitly.");
        Assert.True(sourceCopy > restore, "Project files must be restored before source-only layers are copied.");
        Assert.True(publish > sourceCopy, "The backend publish must run after the source layer is copied.");
    }

    [Fact]
    public void CandidateDockerfiles_ApplyRepositoryBuildPolicy()
    {
        var root = FindRepoRoot();
        var back = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.back"));
        var front = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.front"));

        Assert.Contains("COPY Directory.Build.targets .", back, StringComparison.Ordinal);
        Assert.Equal(2, front.Split("COPY Directory.Build.targets .", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void FrontDockerfile_UsesPinnedStaticServerBuildContext()
    {
        var root = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.front"));
        var project = File.ReadAllText(Path.Combine(root, "deploy", "docker", "StaticServer.csproj"));

        Assert.Contains("COPY Directory.Packages.props .", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY NuGet.config .", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY src/Aetheus.Analyzers/ src/Aetheus.Analyzers/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY packages/aetheus-web-analytics/src/aetheus-web-analytics.js packages/aetheus-web-analytics/src/aetheus-web-analytics.js", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY deploy/docker/StaticServer.csproj deploy/docker/StaticServer.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY deploy/docker/packages.lock.json deploy/docker/packages.lock.json", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet new", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet add package", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet restore StaticServer.csproj --locked-mode", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet publish StaticServer.csproj -c Release -o /server/publish --no-restore", dockerfile,
            StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.Components.WebAssembly.Server", project, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Aetheus.WebAnalytics\\Aetheus.WebAnalytics.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerfilesThatBuildSharedOrAgentCore_CopyExternalEmbeddedResources()
    {
        var root = FindRepoRoot();
        var dockerfiles = new Dictionary<string, string[]>
        {
            ["Dockerfile.agent"] =
            [
                "COPY scanner-manifest.json /scanner-manifest.json",
                "COPY .aetheus/security-rules/opengrep/aetheus-security.yml /.aetheus/security-rules/opengrep/aetheus-security.yml",
                "COPY deploy/scripts/zap-active-automation.sh /deploy/scripts/zap-active-automation.sh",
                "COPY deploy/scripts/publish-observability-package.sh /deploy/scripts/publish-observability-package.sh",
                "COPY deploy/scripts/promote-observability-packages.sh /deploy/scripts/promote-observability-packages.sh"
            ],
            ["Dockerfile.back"] =
            [
                "COPY scanner-manifest.json /scanner-manifest.json",
                "COPY .aetheus/security-rules/opengrep/aetheus-security.yml /.aetheus/security-rules/opengrep/aetheus-security.yml",
                "COPY deploy/scripts/zap-active-automation.sh /deploy/scripts/zap-active-automation.sh",
                "COPY deploy/scripts/publish-observability-package.sh /deploy/scripts/publish-observability-package.sh",
                "COPY deploy/scripts/promote-observability-packages.sh /deploy/scripts/promote-observability-packages.sh"
            ],
            ["Dockerfile.front"] = ["COPY scanner-manifest.json /scanner-manifest.json"],
            ["Dockerfile.vpssim"] =
            [
                "COPY scanner-manifest.json ./",
                "COPY .aetheus/security-rules/opengrep/aetheus-security.yml .aetheus/security-rules/opengrep/aetheus-security.yml",
                "COPY deploy/scripts/zap-active-automation.sh deploy/scripts/zap-active-automation.sh",
                "COPY deploy/scripts/publish-observability-package.sh deploy/scripts/publish-observability-package.sh",
                "COPY deploy/scripts/promote-observability-packages.sh deploy/scripts/promote-observability-packages.sh"
            ]
        };

        foreach (var (fileName, expectedCopies) in dockerfiles)
        {
            var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "docker", fileName));
            Assert.All(expectedCopies, expectedCopy =>
                Assert.Contains(expectedCopy, dockerfile, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void BackDockerfile_AgentBuildStagesCopyAgentCoreExternalEmbeddedResources()
    {
        var root = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.back"));
        var expectedCopies = new[]
        {
            "COPY deploy/scripts/zap-active-automation.sh /deploy/scripts/zap-active-automation.sh",
            "COPY deploy/scripts/publish-observability-package.sh /deploy/scripts/publish-observability-package.sh",
            "COPY deploy/scripts/promote-observability-packages.sh /deploy/scripts/promote-observability-packages.sh"
        };

        AssertStageCopiesResources("agent-linux-build", "agent-windows-build");
        AssertStageCopiesResources("agent-windows-build", "agent-release");

        void AssertStageCopiesResources(string stageName, string nextStageName)
        {
            var stageStart = dockerfile.IndexOf($"AS {stageName}", StringComparison.Ordinal);
            Assert.True(stageStart >= 0, $"The {stageName} stage must exist.");

            var stageEnd = dockerfile.IndexOf($"AS {nextStageName}", stageStart, StringComparison.Ordinal);
            Assert.True(stageEnd > stageStart, $"The {stageName} stage must end before {nextStageName}.");

            var stage = dockerfile[stageStart..stageEnd];
            Assert.All(expectedCopies, expectedCopy =>
                Assert.Contains(expectedCopy, stage, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void BackDockerfile_BackendAndEfStagesCopyBackendExternalEmbeddedResources()
    {
        var root = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.back"));
        var expectedCopies = new[]
        {
            "COPY deploy/pipelines/toto-conformance-fixture/ /deploy/pipelines/toto-conformance-fixture/",
            "COPY deploy/pipelines/toto-vulnerable-fixture/ /deploy/pipelines/toto-vulnerable-fixture/",
            "COPY deploy/pipelines/toto-*.yaml /deploy/pipelines/",
            "COPY deploy/pipeline-templates/*.yaml /deploy/pipeline-templates/",
            "COPY deploy/pipeline-templates/generic/*.yaml /deploy/pipeline-templates/generic/",
            "COPY deploy/scripts/generate-delivery-contract.mjs /deploy/scripts/generate-delivery-contract.mjs",
            "COPY deploy/scripts/verify-delivery-promotion.mjs /deploy/scripts/verify-delivery-promotion.mjs",
            "COPY deploy/scripts/resolve-injected-nuget-packages.mjs /deploy/scripts/resolve-injected-nuget-packages.mjs"
        };

        AssertStageCopiesResources("backend-build", "ef-bundle");
        AssertStageCopiesResources("ef-bundle", "runtime");

        void AssertStageCopiesResources(string stageName, string nextStageName)
        {
            var stageStart = dockerfile.IndexOf($"AS {stageName}", StringComparison.Ordinal);
            Assert.True(stageStart >= 0, $"The {stageName} stage must exist.");

            var stageEnd = dockerfile.IndexOf($"AS {nextStageName}", stageStart, StringComparison.Ordinal);
            Assert.True(stageEnd > stageStart, $"The {stageName} stage must end before {nextStageName}.");

            var stage = dockerfile[stageStart..stageEnd];
            Assert.All(expectedCopies, expectedCopy =>
                Assert.Contains(expectedCopy, stage, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void VpsSimDockerfiles_DefaultToNonRootAndNeverUseImplicitSshPublication()
    {
        var root = FindRepoRoot();
        var ignore = File.ReadAllText(Path.Combine(root, ".trivyignore.yaml"));
        Assert.Contains("misconfigurations: []", ignore, StringComparison.Ordinal);
        Assert.DoesNotContain("AVD-DS-0002", ignore, StringComparison.Ordinal);

        var simulatorDockerfiles = new[]
        {
            File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.vpssim")),
            File.ReadAllText(Path.Combine(root, "deploy", "docker", "Dockerfile.vpssim-blank"))
        };
        Assert.All(simulatorDockerfiles, dockerfile =>
        {
            Assert.Contains("USER vpssim-runtime", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("EXPOSE 22", dockerfile, StringComparison.Ordinal);
        });

        var simulatorComposeFiles = new[]
        {
            File.ReadAllText(Path.Combine(root, "deploy", "compose", "vpssim.compose.yml")),
            File.ReadAllText(Path.Combine(root, "deploy", "compose", "vpssim-blank.compose.yml"))
        };
        Assert.All(simulatorComposeFiles, compose =>
        {
            Assert.Contains("user: \"0:0\"", compose, StringComparison.Ordinal);
            Assert.Contains("127.0.0.1:${VPSSIM_SSH_PORT:-2222}:22", compose, StringComparison.Ordinal);
        });

        var productionDefinitions = RepositoryScan.Enumerate(Path.Combine(root, "deploy"), "*")
            .Where(path => !Path.GetFileName(path).StartsWith("vpssim", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path) is ".yaml" or ".yml" or ".conf" or ".sh")
            .Select(File.ReadAllText)
            .ToList();
        Assert.DoesNotContain(productionDefinitions, definition =>
            definition.Contains("Dockerfile.vpssim", StringComparison.Ordinal));
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
