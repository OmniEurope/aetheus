// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Analysis;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class ScannerCapabilityProbeTests
{
    [Fact]
    public async Task CollectAsync_FreshTrivyMetadataReportsReady()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TemporaryDirectory();
        await WriteMetadataAsync(directory.Path, "2026-07-22T06:00:00Z");
        var diagnostics = await ScannerCapabilityProbe.CollectAsync(
            UnavailableDocker(), directory.Path, new FakeTimeProvider(now), TestContext.Current.CancellationToken);
        Assert.Contains(diagnostics, value => value.StartsWith("scanner-db:trivy:ready:", StringComparison.Ordinal));
        Assert.Contains($"scanner-manifest:sha256:{ScannerManifestCatalog.Sha256}", diagnostics);
    }

    [Fact]
    public async Task CollectAsync_StaleTrivyMetadataReportsDegraded()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        using var directory = new TemporaryDirectory();
        await WriteMetadataAsync(directory.Path, "2026-07-19T06:00:00Z");
        var diagnostics = await ScannerCapabilityProbe.CollectAsync(
            UnavailableDocker(), directory.Path, new FakeTimeProvider(now), TestContext.Current.CancellationToken);
        Assert.Contains(diagnostics, value => value.StartsWith("scanner-db:trivy:degraded:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectAsync_MissingTrivyMetadataIsExplicitlyNotInitialized()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = await ScannerCapabilityProbe.CollectAsync(
            UnavailableDocker(), directory.Path, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Contains("scanner-db:trivy:not-initialized:no vulnerability database cached", diagnostics);
    }

    private static IShellRunner UnavailableDocker()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(1, string.Empty, "unavailable"));
        return shell;
    }

    private static async Task WriteMetadataAsync(string root, string updatedAt)
    {
        var directory = System.IO.Path.Combine(root, "scanner-cache", "trivy", "db");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "metadata.json"),
            $$"""{"UpdatedAt":"{{updatedAt}}"}""", TestContext.Current.CancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"aetheus-scanner-probe-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
