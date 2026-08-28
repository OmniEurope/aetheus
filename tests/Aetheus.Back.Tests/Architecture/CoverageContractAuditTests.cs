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
        Assert.Equal("**/*.g.cs,**/Migrations/*.cs,**/obj/**/*.cs",
            document.Descendants("ExcludeByFile").Single().Value);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
