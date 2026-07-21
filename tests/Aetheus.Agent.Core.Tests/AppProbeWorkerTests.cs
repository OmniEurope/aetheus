// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class AppProbeWorkerTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private static (AppProbeWorker Worker, IServerApiClient Api, FakeTimeProvider Time) Build(
        HttpStatusCode probeStatus = HttpStatusCode.OK)
    {
        var api = Substitute.For<IServerApiClient>();
        api.GetAppProbesAsync(Arg.Any<CancellationToken>()).Returns(new List<AppProbeConfigDto>
        {
            new() { MonitoredAppId = 1, ProbeUrl = "http://localhost/health", ProbeIntervalSeconds = 30, ProbeTimeoutSeconds = 5, ExpectedStatusCode = 200 }
        });

        var enrollment = Substitute.For<IEnrollmentService>();
        enrollment.IsEnrolled.Returns(true);

        var http = new HttpClient(new StubHandler(probeStatus));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(http);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
        var worker = new AppProbeWorker(api, enrollment, factory, time, Substitute.For<ILogger<AppProbeWorker>>());
        return (worker, api, time);
    }

    [Fact]
    public async Task RunTick_ProbesDueApp_AndReports()
    {
        var (worker, api, _) = Build();
        await worker.RunTickAsync(TestContext.Current.CancellationToken);

        await api.Received(1).ReportAppProbeResultsAsync(
            Arg.Is<List<AppProbeResultDto>>(r => r.Count == 1 && r[0].MonitoredAppId == 1 && r[0].IsUp),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTick_DoesNotReProbe_BeforeIntervalElapses()
    {
        var (worker, api, time) = Build();
        await worker.RunTickAsync(TestContext.Current.CancellationToken); // probes, nextDue = +30s

        time.Advance(TimeSpan.FromSeconds(10)); // still before the 30s interval
        await worker.RunTickAsync(TestContext.Current.CancellationToken);

        // Exactly one report (nothing new due -> empty buffer -> no second send).
        await api.Received(1).ReportAppProbeResultsAsync(Arg.Any<List<AppProbeResultDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTick_ReQueuesResults_WhenBackendReportFails()
    {
        var (worker, api, time) = Build();
        var batchSizes = new List<int>();
        api.When(x => x.ReportAppProbeResultsAsync(Arg.Any<List<AppProbeResultDto>>(), Arg.Any<CancellationToken>()))
            .Do(ci => batchSizes.Add(((List<AppProbeResultDto>)ci[0]).Count));
        api.ReportAppProbeResultsAsync(Arg.Any<List<AppProbeResultDto>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("backend down")), Task.CompletedTask);

        await worker.RunTickAsync(TestContext.Current.CancellationToken);       // probe#1 -> send throws -> keep buffered
        time.Advance(TimeSpan.FromSeconds(31));                  // app due again
        await worker.RunTickAsync(TestContext.Current.CancellationToken);       // probe#2 -> send succeeds with BOTH results

        Assert.Equal(2, batchSizes.Count);
        Assert.Equal(1, batchSizes[0]); // first attempt carried 1 result
        Assert.Equal(2, batchSizes[1]); // failed result was re-queued and re-sent alongside the new one
    }
}
