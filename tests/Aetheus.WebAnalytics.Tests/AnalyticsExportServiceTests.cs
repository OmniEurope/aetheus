// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.WebAnalytics.Tests;

public sealed class AnalyticsExportServiceTests
{
    [Fact]
    public async Task ExecuteAsync_FlushesLowTrafficBatchWithinConfiguredInterval()
    {
        var queue = new AnalyticsExportQueue();
        var handler = new AnalyticsExportRecordingHandler();
        using var client = new HttpClient(handler);
        var factory = new AnalyticsHttpClientFactory(client);
        var options = new AetheusWebAnalyticsOptions
        {
            IngestEndpoint = new Uri("https://aetheus.example/api/ingest/web-analytics/v1/events"),
            IngestKey = "ingest-key",
            ExportIntervalMilliseconds = 100
        };
        var service = new AnalyticsExportService(
            queue,
            factory,
            options,
            NullLogger<AnalyticsExportService>.Instance);
        var eventId = Guid.NewGuid();
        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(queue.TryWrite(new AnalyticsExportEvent { EventId = eventId }));
        var received = await handler.Received.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(eventId.ToString(), received.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ingest-key", received.IngestKey);
    }
}
