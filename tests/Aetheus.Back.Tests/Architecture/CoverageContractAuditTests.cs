// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Xml.Linq;

namespace Aetheus.Back.Tests.Architecture;

public sealed class CoverageContractAuditTests
{
    private static readonly string[] ProductSuites =
    [
        "tests/Aetheus.Back.Tests",
        "tests/Aetheus.Front.Tests",
        "tests/Aetheus.Agent.Core.Tests"
    ];

    [Fact]
    public void RunSettings_ExcludesOnlyCanonicalInfrastructureWhitelist()
    {
        var document = XDocument.Load(Path.Combine(RepoRoot(), "coverage.runsettings"));
        var exclude = document.Descendants("Exclude").Single().Value;
        var excludedTypes = exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(
        [
            "[*]Microsoft.AspNetCore.OpenApi.Generated.*",
            "[*]Aetheus.Back.Data.Migrations.*",
            "[*]System.Runtime.CompilerServices.*"
        ], excludedTypes);
        Assert.Equal("**/*.g.cs,**/Migrations/*.cs",
            document.Descendants("ExcludeByFile").Single().Value);
    }

    [Fact]
    public void LocalAndCi_UseSameSettingsAndProductSuites()
    {
        var root = RepoRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-test.yml"));
        var launcher = File.ReadAllText(Path.Combine(root, "scripts", "ylaunch-core.ps1"));
        var linuxLauncher = File.ReadAllText(Path.Combine(root, "ybaunch.sh"));
        var productBlock = ci[ci.IndexOf("Product unit tests with coverage", StringComparison.Ordinal)..ci.IndexOf("Analyzer tests", StringComparison.Ordinal)];

        Assert.Contains("--settings coverage.runsettings", productBlock, StringComparison.Ordinal);
        Assert.Contains("--settings", launcher, StringComparison.Ordinal);
        Assert.Contains("$resultDirectory = if ($Coverage) { $coverageDir } else", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("\"--results-directory\", $coverageDir", launcher, StringComparison.Ordinal);
        Assert.Contains("$runSettings", launcher, StringComparison.Ordinal);
        Assert.Contains("--settings \"$RUN_SETTINGS\"", linuxLauncher, StringComparison.Ordinal);
        foreach (var suite in ProductSuites)
        {
            Assert.Contains(suite, productBlock, StringComparison.Ordinal);
            Assert.Contains(suite.Replace('/', '\\'), launcher, StringComparison.Ordinal);
            Assert.Contains(suite, linuxLauncher, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Aetheus.Analyzers.Tests", productBlock, StringComparison.Ordinal);
        Assert.Contains("TEST_CONFIGURATION=\"Release\"", linuxLauncher, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_IsMergedAndGatedAtSeventyFivePercent()
    {
        var root = RepoRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-test.yml"));
        var launcher = File.ReadAllText(Path.Combine(root, "scripts", "ylaunch-core.ps1"));
        var linuxLauncher = File.ReadAllText(Path.Combine(root, "ybaunch.sh"));
        using var tools = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dotnet-tools.json")));

        Assert.True(tools.RootElement.GetProperty("tools").TryGetProperty("dotnet-reportgenerator-globaltool", out _));
        Assert.Contains("Expected exactly 3 product coverage reports", ci, StringComparison.Ordinal);
        Assert.Contains("./coverage-merged/Cobertura.xml", ci, StringComparison.Ordinal);
        Assert.Contains("MIN_COVERAGE=75", ci, StringComparison.Ordinal);
        Assert.Contains("-reporttypes:Cobertura", ci, StringComparison.Ordinal);
        Assert.Contains("-assemblyfilters:+Aetheus.Back;+Aetheus.Front;+Aetheus.Agent.Core", ci, StringComparison.Ordinal);
        Assert.Contains("sed -i '/^[[:space:]]*<!DOCTYPE coverage /d'", ci, StringComparison.Ordinal);
        Assert.Contains("Expected exactly 3 product coverage reports", launcher, StringComparison.Ordinal);
        Assert.Contains("$_.Directory.Parent.FullName -eq $coverageDir", launcher, StringComparison.Ordinal);
        Assert.Contains("-reporttypes:Html;Cobertura", launcher, StringComparison.Ordinal);
        Assert.Contains("-assemblyfilters:+Aetheus.Back;+Aetheus.Front;+Aetheus.Agent.Core", launcher, StringComparison.Ordinal);
        Assert.Contains("line coverage is below the required 75%", launcher, StringComparison.Ordinal);
        Assert.Contains("Expected exactly 3 product coverage reports", linuxLauncher, StringComparison.Ordinal);
        Assert.Contains("-reporttypes:Html;Cobertura", linuxLauncher, StringComparison.Ordinal);
        Assert.Contains("-assemblyfilters:+Aetheus.Back;+Aetheus.Front;+Aetheus.Agent.Core", linuxLauncher, StringComparison.Ordinal);
        Assert.Contains("line coverage is below the required 75%", linuxLauncher, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
