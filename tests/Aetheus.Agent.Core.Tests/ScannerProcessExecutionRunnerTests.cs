// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class ScannerProcessExecutionRunnerTests
{
    [Fact]
    public async Task RunAsync_CacheGrowthBeyondQuotaCancelsActiveProcess()
    {
        var root = Directory.CreateTempSubdirectory("aetheus-scanner-quota-").FullName;
        var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
        var runner = Substitute.For<IScannerProcessRunner>();
        runner.RunAsync(
                Arg.Any<ProcessStartInfo>(),
                Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return new ExecutorResult(0, false);
            });
        var sut = new ScannerProcessExecutionRunner(runner, TimeProvider.System);
        var scanner = new ScannerManifestEntry
        {
            Key = "gitleaks",
            Name = "Gitleaks",
            MaxReportBytes = 1024,
            MaxCacheBytes = 8
        };

        try
        {
            var execution = sut.RunAsync(
                scanner,
                new ProcessStartInfo("unused"),
                Path.Combine(output, "report.sarif"),
                output,
                cache,
                30,
                (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(cache, "growing-cache.db"),
                new byte[16],
                TestContext.Current.CancellationToken);

            var (result, quotaExceeded) = await execution.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(quotaExceeded);
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(result.TimedOut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
