// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.PackageRegistry;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.PackageRegistry;

/// <summary>
/// Separate opt-in load contract for registry storage. It reports elapsed time through the
/// assertion message on failure while keeping correctness independent from host speed.
/// </summary>
public sealed class PackageRegistryStorageLoadTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "aetheus-registry-load", Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Category", "Load")]
    public async Task ParallelDistinctVersionsLeaveCompletePayloadsWithoutTemporaryFiles()
    {
        var storage = CreateStorage();
        var payload = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        var stopwatch = Stopwatch.StartNew();

        var stored = await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
        {
            await using var input = new MemoryStream(payload, writable: false);
            return await storage.SaveAsync(
                PackageRegistryKind.Npm,
                "load-probe",
                $"1.0.{index}",
                ".tgz",
                input,
                TestContext.Current.CancellationToken);
        }));
        stopwatch.Stop();

        Assert.True(
            stored.Select(item => item.RelativePath).Distinct().Count() == 32,
            $"32-version load probe completed in {stopwatch.ElapsedMilliseconds} ms.");
        Assert.All(stored, item => Assert.Equal(payload.Length, item.SizeBytes));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PackageRegistryStorage CreateStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PackageRegistry:BasePath"] = _directory,
                ["PackageRegistry:MaxPackageBytes"] = "1048576",
                ["PackageRegistry:VolumeQuotaBytes"] = (20 * 1024 * 1024).ToString()
            })
            .Build();
        return new PackageRegistryStorage(configuration);
    }
}
