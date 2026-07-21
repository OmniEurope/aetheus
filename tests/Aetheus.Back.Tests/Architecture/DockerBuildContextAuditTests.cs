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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
