// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Analysis;

namespace Aetheus.Agent.Core.Tests;

public sealed class ScannerContainerProcessBuilderTests
{
    [Fact]
    public void AddRestrictedEgressEnvironment_BypassesOnlyTheScannerControlHost()
    {
        var arguments = new List<string>();

        ScannerContainerProcessBuilder.AddRestrictedEgressEnvironment(arguments, "http://proxy.test:56400");

        Assert.Contains("HTTP_PROXY=http://proxy.test:56400", arguments);
        Assert.Contains("HTTPS_PROXY=http://proxy.test:56400", arguments);
        Assert.Contains("http_proxy=http://proxy.test:56400", arguments);
        Assert.Contains("https_proxy=http://proxy.test:56400", arguments);
        Assert.Contains("NO_PROXY=localhost", arguments);
        Assert.Contains("no_proxy=localhost", arguments);
        Assert.DoesNotContain("NO_PROXY=127.0.0.1", arguments);
        Assert.DoesNotContain("no_proxy=127.0.0.1", arguments);
    }

    [Fact]
    public void Build_GitleaksRangeIsOneValidatedArgumentAfterTheImage()
    {
        var range = $"{new string('a', 40)}..{new string('b', 40)}";
        var scanner = new ScannerManifestEntry
        {
            Key = "gitleaks-history",
            Image = $"example.invalid/gitleaks@sha256:{new string('c', 64)}",
            EntryPoint = "gitleaks",
            Execution = "container",
            Arguments = ["git", "/src"],
            ReportPath = "report.sarif"
        };

        var process = ScannerContainerProcessBuilder.Build(
            scanner,
            Directory.GetCurrentDirectory(),
            Directory.GetCurrentDirectory(),
            null,
            null,
            new Dictionary<string, string> { ["AETHEUS_GITLEAKS_LOG_OPTIONS"] = range },
            null);

        var arguments = process.ArgumentList.ToList();
        var imageIndex = arguments.IndexOf(scanner.Image);
        var optionIndex = arguments.IndexOf("--log-opts");
        Assert.True(imageIndex >= 0);
        Assert.True(optionIndex > imageIndex);
        Assert.Equal(range, arguments[optionIndex + 1]);
        Assert.DoesNotContain("--all", arguments);
    }

    [Fact]
    public void Build_GitleaksFullScanDoesNotAddLogOptions()
    {
        var scanner = new ScannerManifestEntry
        {
            Key = "gitleaks-history",
            Image = $"example.invalid/gitleaks@sha256:{new string('c', 64)}",
            EntryPoint = "gitleaks",
            Execution = "container",
            Arguments = ["git", "/src"],
            ReportPath = "report.sarif"
        };

        var process = ScannerContainerProcessBuilder.Build(
            scanner,
            Directory.GetCurrentDirectory(),
            Directory.GetCurrentDirectory(),
            null,
            null,
            new Dictionary<string, string>(),
            null);

        Assert.DoesNotContain("--log-opts", process.ArgumentList);
    }

    [Fact]
    public void Build_ZapApiAppliesBoundedHeapAndContainerResources()
    {
        var scanner = Assert.Single(
            ScannerManifestCatalog.Default.Scanners,
            entry => entry.Key == "zap-api");

        var process = ScannerContainerProcessBuilder.Build(
            scanner,
            Directory.GetCurrentDirectory(),
            Directory.GetCurrentDirectory(),
            null,
            null,
            new Dictionary<string, string>
            {
                ["AETHEUS_DAST_TARGET_URL"] = "http://127.0.0.1:8080",
                ["AETHEUS_DAST_API_SPECIFICATION_URL"] = "http://127.0.0.1:8080/openapi/v1.json",
                ["AETHEUS_DAST_API_SPECIFICATION_FORMAT"] = "openapi",
            },
            null);

        var arguments = process.ArgumentList.ToList();
        Assert.Equal("4g", OptionValue(arguments, "--memory"));
        Assert.Equal("2.0", OptionValue(arguments, "--cpus"));
        Assert.Contains(
            "_JAVA_OPTIONS=-Duser.home=/home/zap/runtime -Xmx2048m -XX:ActiveProcessorCount=2",
            arguments);

        static string OptionValue(List<string> arguments, string option)
        {
            var index = arguments.IndexOf(option);
            Assert.InRange(index, 0, arguments.Count - 2);
            return arguments[index + 1];
        }
    }
}
