// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Analysis;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class RestrictedScannerEgressTests
{
    [Fact]
    public async Task StartAsync_NetworkScannerWithoutAllowlistFailsClosed()
    {
        var runner = Substitute.For<IScannerProcessRunner>();
        var scanner = new ScannerManifestEntry { Execution = "container", Network = "bridge" };

        var exception = await Assert.ThrowsAsync<IOException>(() => RestrictedScannerEgress.StartAsync(
            scanner, new Dictionary<string, string>(), runner, (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken));

        Assert.Contains("allowlist", exception.Message, StringComparison.OrdinalIgnoreCase);
        await runner.DidNotReceive().RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<int>(),
            Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>());
    }
}
