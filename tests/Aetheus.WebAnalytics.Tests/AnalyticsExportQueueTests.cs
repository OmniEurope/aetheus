// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.WebAnalytics.Tests;

public sealed class AnalyticsExportQueueTests
{
    [Fact]
    public void TryWrite_RejectsSynchronouslyWhenBoundedQueueIsFull()
    {
        var queue = new AnalyticsExportQueue();
        var analyticsEvent = new AnalyticsExportEvent();

        for (var index = 0; index < 2048; index++)
            Assert.True(queue.TryWrite(analyticsEvent));

        Assert.False(queue.TryWrite(analyticsEvent));
    }

    [Fact]
    [Trait("Category", "Load")]
    public void SaturatedQueue_DropsOneHundredThousandEventsWithBoundedAllocationAndThroughput()
    {
        const int attempts = 100_000;
        var queue = new AnalyticsExportQueue();
        var analyticsEvent = new AnalyticsExportEvent();
        for (var index = 0; index < 2048; index++)
            Assert.True(queue.TryWrite(analyticsEvent));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var unexpectedlyAccepted = 0;
        for (var index = 0; index < attempts; index++)
        {
            if (queue.TryWrite(analyticsEvent))
                unexpectedlyAccepted++;
        }
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var eventsPerSecond = attempts / stopwatch.Elapsed.TotalSeconds;
        var bytesPerAttempt = allocatedBytes / (double)attempts;

        TestContext.Current.SendDiagnosticMessage(
            $"Saturated queue: {eventsPerSecond:N0} events/s, "
            + $"{bytesPerAttempt:N1} allocated bytes/attempt.");
        Assert.Equal(0, unexpectedlyAccepted);
        Assert.True(eventsPerSecond >= 10_000, $"Throughput was {eventsPerSecond:N0} events/s.");
        Assert.True(bytesPerAttempt <= 256, $"Allocation was {bytesPerAttempt:N1} bytes/attempt.");
    }
}
