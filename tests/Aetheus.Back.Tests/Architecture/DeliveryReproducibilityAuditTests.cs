// SPDX-License-Identifier: EUPL-1.2

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

public sealed partial class DeliveryReproducibilityAuditTests
{
    private const string InstallerCommit = "da3ce11ba63f3dbb0fb835d41bda2665d5c48e84";
    private const string InstallerSha256 = "082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e";
    private static string Root => FindRepoRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [GeneratedRegex(@"(?m)^\s*(?:FROM|image:)\s+(?<image>(?:mcr\.microsoft\.com/|ubuntu:|postgres:)[^\s]+)")]
    private static partial Regex ExternalImageRegex();

    [Fact]
    public void ExternalContainerImagesAndHostedRunnerGeneration_ArePinned()
    {
        var deploymentFiles = Directory.EnumerateFiles(Path.Combine(Root, "deploy"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, ".github", "workflows"), "*", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.Ordinal)
                           || new[] { ".yml", ".yaml", ".sh" }.Contains(Path.GetExtension(path), StringComparer.Ordinal))
            .ToList();

        var unpinnedImages = deploymentFiles
            .SelectMany(path => ExternalImageRegex().Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(Root, path)}: {match.Groups["image"].Value}"))
            .Where(match => !match.Contains("@sha256:", StringComparison.Ordinal))
            .ToList();
        Assert.True(unpinnedImages.Count == 0,
            "Every external container image must retain a readable tag plus an immutable digest:\n  "
            + string.Join("\n  ", unpinnedImages));

        var workflows = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText));
        Assert.DoesNotContain("ubuntu-latest", workflows, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflows, StringComparison.Ordinal);
        Assert.All(Regex.Matches(workflows, @"uses:\s*[^@\s]+@(?<reference>[^\s]+)")
            .Select(match => match.Groups["reference"].Value), reference =>
            Assert.Matches("^[0-9a-f]{40}$", reference));
        Assert.Contains("actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7.0.0", workflows, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0", workflows, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1", workflows, StringComparison.Ordinal);

        var e2eInputs = Read("deploy", "docker", "Dockerfile.e2e");
        Assert.DoesNotContain("mcr.microsoft.com/playwright/dotnet:v1.61.0-noble\"", e2eInputs, StringComparison.Ordinal);
        Assert.Contains("mcr.microsoft.com/playwright/dotnet:v1.61.0-noble@sha256:", e2eInputs, StringComparison.Ordinal);

        var backendDockerfile = Read("deploy", "docker", "Dockerfile.back");
        Assert.Contains("dotnet tool install --global dotnet-ef --version 10.0.10", backendDockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY src/Aetheus.Analyzers/Aetheus.Analyzers.csproj Aetheus.Analyzers/", backendDockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY src/Aetheus.Analyzers/ Aetheus.Analyzers/", backendDockerfile, StringComparison.Ordinal);

        var frontendDockerfile = Read("deploy", "docker", "Dockerfile.front");
        Assert.Contains("COPY src/Aetheus.Analyzers/Aetheus.Analyzers.csproj Aetheus.Analyzers/", frontendDockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY src/Aetheus.Analyzers/ Aetheus.Analyzers/", frontendDockerfile, StringComparison.Ordinal);

        using var globalJson = JsonDocument.Parse(Read("global.json"));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        var sdkVersion = sdk.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sdkVersion));
        var sdkImageReferences = deploymentFiles
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                @"mcr\.microsoft\.com/dotnet/sdk:(?<version>[^@\s]+)@sha256:[0-9a-f]{64}"))
            .ToList();
        Assert.NotEmpty(sdkImageReferences);
        Assert.All(sdkImageReferences, match => Assert.Equal(sdkVersion, match.Groups["version"].Value));

        var buildProps = Read("Directory.Build.props");
        var runtimeVersion = Regex.Match(
            buildProps,
            @"<AetheusRuntimeImageVersion>(?<version>[^<]+)</AetheusRuntimeImageVersion>").Groups["version"].Value;
        Assert.False(string.IsNullOrWhiteSpace(runtimeVersion));
        var frontProject = Read("src", "Aetheus.Front", "Aetheus.Front.csproj");
        Assert.Contains("<KnownWebAssemblySdkPack Update=\"Microsoft.NET.Sdk.WebAssembly.Pack\"", frontProject, StringComparison.Ordinal);
        Assert.Contains("WebAssemblySdkPackVersion=\"$(AetheusRuntimeImageVersion)\"", frontProject, StringComparison.Ordinal);
        Assert.Contains("<KnownAspNetCorePack Update=\"Microsoft.AspNetCore.App.Internal.Assets\"", frontProject, StringComparison.Ordinal);
        Assert.Contains("AspNetCorePackVersion=\"$(AetheusRuntimeImageVersion)\"", frontProject, StringComparison.Ordinal);
        var runtimeImageReferences = deploymentFiles
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                @"mcr\.microsoft\.com/dotnet/(?:aspnet|runtime):(?<version>[^@\s]+)@sha256:[0-9a-f]{64}"))
            .ToList();
        Assert.NotEmpty(runtimeImageReferences);
        Assert.All(runtimeImageReferences, match => Assert.Equal(runtimeVersion, match.Groups["version"].Value));
    }

    [Fact]
    public void RemoteDotnetInstallers_AreCommitPinnedAndHashVerified()
    {
        var candidates = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsNestedWorktree(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => new[] { ".sh", ".yaml", ".yml" }.Contains(Path.GetExtension(path), StringComparer.Ordinal)
                           || Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.Ordinal))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file => file.Source.Contains("dotnet-install.sh", StringComparison.Ordinal))
            .ToList();

        Assert.True(candidates.Count >= 5, "The dotnet-installer scan is unexpectedly small.");
        Assert.All(candidates, file =>
        {
            Assert.DoesNotContain("https://dot.net/v1/dotnet-install.sh", file.Source, StringComparison.Ordinal);
            Assert.Contains(InstallerCommit, file.Source, StringComparison.Ordinal);
            Assert.Contains(InstallerSha256, file.Source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sha256sum", file.Source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FrontRuntimeConfiguration_IsWrittenAsJsonWithoutSedInterpolation()
    {
        var entrypoint = Read("deploy", "docker", "entrypoint-front.sh");
        var server = Read("deploy", "docker", "StaticServer.Program.cs");
        Assert.DoesNotMatch(@"(?m)^\s*sed\b", entrypoint);
        Assert.Contains("JsonNode.Parse", server, StringComparison.Ordinal);
        Assert.Contains("settings[\"ApiBaseUrl\"] = apiBaseUrl", server, StringComparison.Ordinal);
        Assert.Contains("appSettings[\"Version\"] = appVersion", server, StringComparison.Ordinal);
        Assert.Contains("File.Move(temporaryPath, settingsPath, overwrite: true)", server, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProjectHasALockFile_AndCiRestoresInLockedMode()
    {
        var projectFiles = Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsNestedWorktree(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(projectFiles.Count >= 14, "The project scan is unexpectedly small.");

        var missingLocks = projectFiles
            .Where(project => !File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")))
            .Select(project => Path.GetRelativePath(Root, project))
            .ToList();
        Assert.True(missingLocks.Count == 0,
            "Every project must commit its NuGet lock file:\n  " + string.Join("\n  ", missingLocks));

        var buildProps = Read("Directory.Build.props");
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", buildProps, StringComparison.Ordinal);
        Assert.Contains("<NuGetLockFilePath Condition=\"'$(Configuration)' != 'Release'\">", buildProps, StringComparison.Ordinal);
        Assert.Contains("obj\\packages.$(Configuration).lock.json", buildProps, StringComparison.Ordinal);
        Assert.Contains("<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>", buildProps, StringComparison.Ordinal);

        var workflows = Read(".github", "workflows", "build-test.yml");
        Assert.DoesNotContain("run: dotnet restore\n", workflows.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("dotnet restore --locked-mode -p:Configuration=Release", workflows, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static bool IsNestedWorktree(string path) => path.Contains(
        $"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}",
        StringComparison.OrdinalIgnoreCase);
}
