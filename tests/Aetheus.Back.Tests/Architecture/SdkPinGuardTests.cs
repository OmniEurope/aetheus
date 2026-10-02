// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// STD-SDKPIN (ADR-048): the three Microsoft.CodeAnalysis.* packages stay on the Roslyn version shipped
/// by the compiler of the global.json SDK floor. An analyzer compiled against a newer Roslyn than the
/// host compiler is rejected (CS9057) on every machine still at the floor. The guard checks the pin,
/// never the floor itself: moving global.json is a deliberate commit that moves both files together.
/// Adapted from the _Generic kit template docs/tests-template/SdkPinGuardTests.cs (NUnit there).
/// </summary>
public sealed class SdkPinGuardTests
{
    /// <summary>SDK feature band of the floor -> Roslyn version its compiler ships.</summary>
    private static readonly (string Band, string Roslyn)[] RoslynByBand =
    [
        ("9.0.1", "4.12.0"),
        ("10.0.1", "5.0.0"),
        ("10.0.2", "5.3.0"),
        ("10.0.3", "5.6.0"),
        ("10.0.4", "5.9.0"),
    ];

    private static readonly string[] PinnedPackages =
    [
        "Microsoft.CodeAnalysis.CSharp",
        "Microsoft.CodeAnalysis.CSharp.Workspaces",
        "Microsoft.CodeAnalysis.Workspaces.Common",
    ];

    [Fact]
    public void CodeAnalysisPackagesMatchTheSdkFloorCompiler()
    {
        var floor = ReadSdkFloor(Path.Combine(RepositoryScan.Root, "global.json"));
        var expected = RoslynByBand.FirstOrDefault(entry => floor.StartsWith(entry.Band, StringComparison.Ordinal)).Roslyn;
        Assert.True(expected is not null,
            $"SDK floor {floor} is missing from the STD-SDKPIN table: extend RoslynByBand and the kit's code-rules.md.");

        var packagesProps = File.ReadAllText(Path.Combine(RepositoryScan.Root, "Directory.Packages.props"));
        var versions = PinnedPackages
            .Select(package => (Package: package, Version: ReadPackageVersion(packagesProps, package)))
            .ToList();
        Assert.All(versions, entry => Assert.True(entry.Version is not null, $"{entry.Package} is not pinned in Directory.Packages.props."));

        var mismatches = versions
            .Where(entry => entry.Version != expected)
            .Select(entry => $"{entry.Package} = {entry.Version} (expected {expected})")
            .ToList();
        Assert.True(mismatches.Count == 0,
            $"SDK floor {floor} ships Roslyn {expected}: " + string.Join("; ", mismatches));
    }

    private static string ReadSdkFloor(string globalJsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
        var version = document.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version), "global.json declares no sdk.version.");
        return version!;
    }

    /// <summary>Version of a PackageVersion/PackageReference entry, or null when the package is absent.</summary>
    private static string? ReadPackageVersion(string content, string package)
    {
        var match = Regex.Match(
            content,
            @"<Package(?:Version|Reference)\s+Include=""" + Regex.Escape(package) + @"""\s+Version=""(?<version>[^""]+)""");
        return match.Success ? match.Groups["version"].Value : null;
    }
}
