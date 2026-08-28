// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Tests.Architecture;

public sealed class StaticAssetFingerprintTests
{
    [Fact]
    public void AppStylesheet_UsesSdkFingerprintPlaceholder()
    {
        var root = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "Aetheus.Front.csproj"));
        var index = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "index.html"));

        Assert.Contains("StaticWebAssetFingerprintPattern", project, StringComparison.Ordinal);
        Assert.Contains("Pattern=\"css/*.css\"", project, StringComparison.Ordinal);
        Assert.Contains("css/app#[.{fingerprint}].css", index, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"css/app.css\"", index, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
