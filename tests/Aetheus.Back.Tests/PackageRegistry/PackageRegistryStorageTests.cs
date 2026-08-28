// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Back.Components.PackageRegistry;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.PackageRegistry;

public class PackageRegistryStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "aetheus-registry-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndOpen_RoundTripsContentWithoutUsingPackageNameAsPath()
    {
        var storage = CreateStorage();
        var bytes = Encoding.UTF8.GetBytes("immutable package payload");
        await using var input = new MemoryStream(bytes);

        var stored = await storage.SaveAsync(
            PackageRegistryKind.Npm,
            "@scope/package",
            "1.0.0",
            ".tgz",
            input,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("scope", stored.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package", Path.GetDirectoryName(stored.RelativePath)!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(64, stored.Sha256.Length);
        Assert.StartsWith("sha512-", stored.Integrity);
        await using var opened = storage.OpenRead(stored.RelativePath);
        Assert.NotNull(opened);
        using var reader = new StreamReader(opened!, Encoding.UTF8);
        Assert.Equal("immutable package payload", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_RejectsASecondWriteToTheSameImmutableVersion()
    {
        var storage = CreateStorage();
        await using var first = new MemoryStream([1, 2, 3]);
        await storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", first,
            TestContext.Current.CancellationToken);
        await using var second = new MemoryStream([4, 5, 6]);

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.ConflictException>(() => storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", second,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_AdoptsAnIdenticalOrphanedPayload()
    {
        var storage = CreateStorage();
        await using var first = new MemoryStream([1, 2, 3]);
        var initial = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", first,
            TestContext.Current.CancellationToken);
        await using var retry = new MemoryStream([1, 2, 3]);

        var adopted = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", retry,
            TestContext.Current.CancellationToken);

        Assert.Equal(initial.RelativePath, adopted.RelativePath);
        Assert.Equal(initial.Sha256, adopted.Sha256);
        Assert.True(initial.CreatedNew);
        Assert.False(adopted.CreatedNew);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_AdoptsAnIdenticalPayloadWhenVolumeIsAtQuota()
    {
        const int payloadSize = 1024 * 1024;
        var storage = CreateStorage(volumeQuotaBytes: payloadSize);
        await using var first = new MemoryStream(new byte[payloadSize]);
        var initial = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", first,
            TestContext.Current.CancellationToken);
        await using var retry = new MemoryStream(new byte[payloadSize]);

        var adopted = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "sample", "1.0.0", ".nupkg", retry,
            TestContext.Current.CancellationToken);

        Assert.Equal(initial.RelativePath, adopted.RelativePath);
        Assert.Equal(initial.Sha256, adopted.Sha256);
        Assert.False(adopted.CreatedNew);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_RejectsPromotionWhenGlobalVolumeQuotaWouldBeExceeded()
    {
        var storage = CreateStorage(volumeQuotaBytes: 1024 * 1024);
        await using var first = new MemoryStream(new byte[700 * 1024]);
        await storage.SaveAsync(
            PackageRegistryKind.NuGet, "first", "1.0.0", ".nupkg", first,
            TestContext.Current.CancellationToken);
        await using var second = new MemoryStream(new byte[700 * 1024]);

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.BadRequestException>(() => storage.SaveAsync(
            PackageRegistryKind.NuGet, "second", "1.0.0", ".nupkg", second,
            TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Reconcile_RemovesOnlyOldUnreferencedPayloads()
    {
        var storage = CreateStorage();
        await using var referencedInput = new MemoryStream([1, 2, 3]);
        var referenced = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "kept", "1.0.0", ".nupkg", referencedInput,
            TestContext.Current.CancellationToken);
        await using var orphanInput = new MemoryStream([4, 5, 6]);
        var orphan = await storage.SaveAsync(
            PackageRegistryKind.NuGet, "orphan", "1.0.0", ".nupkg", orphanInput,
            TestContext.Current.CancellationToken);
        var orphanPath = Path.Combine(_directory, orphan.RelativePath);
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddHours(-2));

        var deleted = await storage.ReconcileAsync(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { referenced.RelativePath },
            DateTime.UtcNow.AddHours(-1),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted);
        using var kept = storage.OpenRead(referenced.RelativePath);
        Assert.NotNull(kept);
        Assert.Null(storage.OpenRead(orphan.RelativePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PackageRegistryStorage CreateStorage(long volumeQuotaBytes = 20 * 1024 * 1024)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PackageRegistry:BasePath"] = _directory,
                ["PackageRegistry:MaxPackageBytes"] = "1048576",
                ["PackageRegistry:VolumeQuotaBytes"] = volumeQuotaBytes.ToString()
            })
            .Build();
        return new PackageRegistryStorage(configuration);
    }
}
