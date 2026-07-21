// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// No-fake proof of the probe path: the worker makes a REAL TCP HTTP request against a local
/// listener and the reported result reflects the actual HTTP status (not a stubbed handler).
/// </summary>
public sealed class AppProbeWorkerHttpTests
{
    private static (AppProbeWorker Worker, IServerApiClient Api) BuildWorker(string url, int expectedStatus)
    {
        var api = Substitute.For<IServerApiClient>();
        api.GetAppProbesAsync(Arg.Any<CancellationToken>()).Returns(new List<AppProbeConfigDto>
        {
            new() { MonitoredAppId = 1, ProbeUrl = url, ProbeIntervalSeconds = 30, ProbeTimeoutSeconds = 5, ExpectedStatusCode = expectedStatus }
        });

        var enrollment = Substitute.For<IEnrollmentService>();
        enrollment.IsEnrolled.Returns(true);

        // Real HttpClient over a real socket handler - no message-handler stub.
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }));

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
        var worker = new AppProbeWorker(api, enrollment, factory, time, Substitute.For<ILogger<AppProbeWorker>>());
        return (worker, api);
    }

    private static async Task WithListener(int status, Func<string, Task> body)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://127.0.0.1:{port}/";
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            var requestBuffer = new byte[4096];
            _ = await stream.ReadAsync(requestBuffer, TestContext.Current.CancellationToken);
            var response = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} Test\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        await body(url);
        await serve.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RealHttp_ExpectedStatus_ReportsUp()
    {
        await WithListener(200, async url =>
        {
            var (worker, api) = BuildWorker(url, expectedStatus: 200);
            await worker.RunTickAsync(TestContext.Current.CancellationToken);

            await api.Received(1).ReportAppProbeResultsAsync(
                Arg.Is<List<AppProbeResultDto>>(r => r.Count == 1 && r[0].IsUp && r[0].StatusCode == 200),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RealHttp_UnexpectedStatus_ReportsDown_WithActualCode()
    {
        await WithListener(500, async url =>
        {
            var (worker, api) = BuildWorker(url, expectedStatus: 200);
            await worker.RunTickAsync(TestContext.Current.CancellationToken);

            await api.Received(1).ReportAppProbeResultsAsync(
                Arg.Is<List<AppProbeResultDto>>(r => r.Count == 1 && !r[0].IsUp && r[0].StatusCode == 500),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RealHttp_ConnectionRefused_ReportsDown_NoStatus()
    {
        // Keep the endpoint bound but deliberately not listening: no other process can claim the port,
        // and the real connect attempt is deterministically refused without a free-port TOCTOU window.
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        var (worker, api) = BuildWorker($"http://127.0.0.1:{port}/", expectedStatus: 200);
        await worker.RunTickAsync(TestContext.Current.CancellationToken);

        await api.Received(1).ReportAppProbeResultsAsync(
            Arg.Is<List<AppProbeResultDto>>(r => r.Count == 1 && !r[0].IsUp && r[0].StatusCode == null && r[0].Error != null),
            Arg.Any<CancellationToken>());
    }
}
