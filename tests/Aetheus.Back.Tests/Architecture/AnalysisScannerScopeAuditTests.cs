// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Tests.Architecture;

public sealed class AnalysisScannerScopeAuditTests
{
    [Fact]
    public void Jscpd_MeasuresMaintainedProductCodeWithoutTestOrGeneratedNoise()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ".jscpd.json")));
        var ignored = document.RootElement.GetProperty("ignore")
            .EnumerateArray()
            .Select(item => item.GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("tests/**", ignored);
        Assert.Contains(".codex/**", ignored);
        Assert.DoesNotContain(".Codex/**", ignored);
        Assert.Contains("packages/**/test/**", ignored);
        Assert.Contains("**/Data/Migrations/**", ignored);
        Assert.Contains("src/Aetheus.Back/Data/DbInitializer.cs", ignored);
        Assert.DoesNotContain("src/**", ignored);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
