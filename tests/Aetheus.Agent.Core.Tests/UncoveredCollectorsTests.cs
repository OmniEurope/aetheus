// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Smoke tests that exercise CollectAsync on collectors which shell out to system binaries.
/// On a CI/dev box without the target binaries, the collector falls back to IsInstalled=false
/// via the detect-binary path; this still covers the happy entry point, catch-all fallback,
/// and the DTO shape.
/// </summary>
public class UncoveredCollectorsTests
{
    // A substituted IShellRunner returns empty output for the detect-binary probe, so every
    // collector must degrade to its "not installed" fallback DTO - assert that shape, not just
    // non-null (a filler Assert.NotNull would pass on any render and prove nothing).

    [Fact]
    public async Task ApacheCollector_CollectAsync_NotInstalled_ReturnsEmptyFallback()
    {
        var sut = new ApacheCollector(NullLogger<ApacheCollector>.Instance, Substitute.For<IShellRunner>());
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
        Assert.Empty(result.VirtualHosts);
    }

    [Fact]
    public async Task CertbotCollector_CollectAsync_NotInstalled_ReturnsEmptyFallback()
    {
        var sut = new CertbotCollector(NullLogger<CertbotCollector>.Instance, Substitute.For<IShellRunner>());
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
        Assert.Empty(result.Certificates);
    }

    [Fact]
    public async Task MailCollector_CollectAsync_NotInstalled_ReturnsFallback()
    {
        var sut = new MailCollector(NullLogger<MailCollector>.Instance, Substitute.For<IShellRunner>());
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
    }

    [Fact]
    public async Task PortsentryCollector_CollectAsync_NotInstalled_ReturnsFallback()
    {
        var sut = new PortsentryCollector(NullLogger<PortsentryCollector>.Instance, Substitute.For<IShellRunner>());
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
    }

    [Fact]
    public async Task RkhunterCollector_CollectAsync_NotInstalled_ReturnsFallback()
    {
        // fileExists: _ => false makes detection hermetic - otherwise the real host FS (a CI agent with
        // rkhunter installed) flips IsInstalled true and the "not installed" assertion fails.
        var sut = new RkhunterCollector(NullLogger<RkhunterCollector>.Instance, Substitute.For<IShellRunner>(), fileExists: _ => false);
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
    }

    [Fact]
    public async Task TeamspeakCollector_CollectAsync_NotInstalled_ReturnsFallback()
    {
        var sut = new TeamspeakCollector(NullLogger<TeamspeakCollector>.Instance, Substitute.For<IShellRunner>(), Substitute.For<ITeamspeakQueryClient>(), fileExists: _ => false);
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsInstalled);
    }

    [Fact]
    public async Task ApacheCollector_CollectAsync_Cancelled_PropagatesCancellation()
    {
        var sut = new ApacheCollector(NullLogger<ApacheCollector>.Instance, Substitute.For<IShellRunner>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.CollectAsync(cts.Token));
    }

    [Fact]
    public async Task DockerCollector_CollectAllAsync_NotInstalled_ReturnsEmptyCollections()
    {
        var sut = new DockerCollector(NullLogger<DockerCollector>.Instance, Substitute.For<IShellRunner>());
        var result = await sut.CollectAllAsync(TestContext.Current.CancellationToken);
        Assert.Empty(result.Containers);
        Assert.Empty(result.Images);
    }

    // --- SudoersHashCollector (S-TECH-15) ---

    [Fact]
    public async Task SudoersHashCollector_CollectAsync_ReturnsEmpty_WhenNoSudoersFiles()
    {
        // On a dev/CI box the /etc/sudoers.d/aetheus-* files don't exist (or are
        // unreadable) - the collector must degrade gracefully to an empty dict, never throw.
        var sut = new SudoersHashCollector(fileExists: _ => false);
        var result = await sut.CollectAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        // No aetheus sudoers drop-ins on the test host → empty (no false drift signal).
        Assert.Empty(result);
    }

    [Fact]
    public async Task SudoersHashCollector_CollectAsync_Cancellable()
    {
        var sut = new SudoersHashCollector();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.CollectAsync(cts.Token));
    }
}
