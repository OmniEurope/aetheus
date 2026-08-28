// SPDX-License-Identifier: EUPL-1.2
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Aetheus.Back.Components.PackageRegistry;

namespace Aetheus.Back.Tests.PackageRegistry;

public class PackageRegistryInspectorTests
{
    [Fact]
    public async Task NuGetInspector_ReadsIdentityAndDependenciesFromNuspec()
    {
        await using var package = CreateNuGetPackage();

        var result = await new NuGetPackageInspector().InspectAsync(
            package, TestContext.Current.CancellationToken);

        Assert.Equal("Aetheus.Telemetry", result.Name);
        Assert.Equal("aetheus.telemetry", result.NormalizedName);
        Assert.Equal("1.2.3-beta.1", result.NormalizedVersion);
        Assert.True(result.IsPrerelease);
        var group = Assert.Single(result.DependencyGroups);
        Assert.Equal("net8.0", group.TargetFramework);
        var dependency = Assert.Single(group.Dependencies);
        Assert.Equal("OpenTelemetry", dependency.Id);
        Assert.Equal("[1.9.0, 2.0.0)", dependency.Range);
    }

    [Fact]
    public async Task NpmInspector_ReadsScopedIdentityFromTarball()
    {
        await using var package = CreateNpmPackage();

        var result = await new NpmPackageInspector().InspectAsync(
            package, TestContext.Current.CancellationToken);

        Assert.Equal("@aetheus/telemetry", result.Name);
        Assert.Equal("2.4.0", result.Version);
        Assert.False(result.IsPrerelease);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("UPPERCASE")]
    [InlineData("@scope")]
    [InlineData("@scope/Bad")]
    public void NpmInspector_RejectsUnsafeNames(string name)
        => Assert.Throws<Aetheus.Back.Exceptions.BadRequestException>(
            () => NpmPackageInspector.ValidatePackageName(name));

    [Fact]
    public async Task NpmInspector_RejectsMalformedGzipAsBadRequest()
    {
        await using var package = new MemoryStream([1, 2, 3, 4]);

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.BadRequestException>(() =>
            new NpmPackageInspector().InspectAsync(package, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NuGetInspector_RejectsMalformedArchiveAsBadRequest()
    {
        await using var package = new MemoryStream([1, 2, 3, 4]);

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.BadRequestException>(() =>
            new NuGetPackageInspector().InspectAsync(package, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NuGetInspector_RejectsArchiveWithUnsafeCompressionRatio()
    {
        await using var package = CreateNuGetPackage(archive =>
        {
            var entry = archive.CreateEntry("content/zeros.bin", CompressionLevel.SmallestSize);
            using var content = entry.Open();
            content.Write(new byte[2 * 1024 * 1024]);
        });

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.BadRequestException>(() =>
            new NuGetPackageInspector().InspectAsync(package, TestContext.Current.CancellationToken));
    }

    private static MemoryStream CreateNuGetPackage(Action<ZipArchive>? addEntries = null)
    {
        const string nuspec = """
            <?xml version="1.0"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Aetheus.Telemetry</id>
                <version>1.2.3-beta.1</version>
                <authors>Aetheus</authors>
                <description>Internal telemetry package.</description>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="OpenTelemetry" version="[1.9.0, 2.0.0)" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Aetheus.Telemetry.nuspec");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                writer.Write(nuspec);
            addEntries?.Invoke(archive);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateNpmPackage()
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            var json = new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"name\":\"@aetheus/telemetry\",\"version\":\"2.4.0\"}"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/package.json")
            {
                DataStream = json
            });
        }
        output.Position = 0;
        return output;
    }
}
