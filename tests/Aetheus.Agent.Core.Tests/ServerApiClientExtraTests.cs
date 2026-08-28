// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class ServerApiClientExtraTests
{
    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool? LastExpectContinue { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastContentType { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastExpectContinue = request.Headers.ExpectContinue;
            LastMethod = request.Method;
            LastPath = request.RequestUri?.PathAndQuery;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }

    private sealed class DelayedReportHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int BodyLength { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            Calls++;
            await Task.Delay(delay, ct);
            BodyLength = (await request.Content!.ReadAsStringAsync(ct)).Length;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new AnalysisReportDto { Id = 7 })
            };
        }
    }

    private static (ServerApiClient Sut, StatusHandler Handler) Build(HttpStatusCode status)
    {
        var handler = new StatusHandler(status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5301/") };
        var factory = Substitute.For<IHttpClientFactory>();
        // Both named clients (RPC "AetheusServer" + streamed "AetheusServerTransfer")
        // resolve to the same handler here - the split only matters for resilience/timeout.
        factory.CreateClient(Arg.Any<string>()).Returns(http);
        return (new ServerApiClient(factory, new AgentState(TimeProvider.System)), handler);
    }

    [Fact]
    public async Task ReportUpdateProgressAsync_Success_SendsRequest()
    {
        var (sut, handler) = Build(HttpStatusCode.OK);

        await sut.ReportUpdateProgressAsync(
            7,
            new AgentUpdateProgressReport { Phase = AgentUpdatePhase.Downloading, Percent = 20 },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/servers/7/agent/progress", handler.LastPath);
        Assert.Contains("\"percent\":20", handler.LastBody);
    }

    [Fact]
    public async Task ReportUpdateProgressAsync_ServerError_Swallowed(
        )
    {
        var (sut, _) = Build(HttpStatusCode.InternalServerError);

        // Best-effort telemetry: a failure must never bubble up to abort a self-update.
        await sut.ReportUpdateProgressAsync(
            7,
            new AgentUpdateProgressReport { Phase = AgentUpdatePhase.Failed, Percent = 0 },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishComplexityAsync_Success_SendsRequest()
    {
        var (sut, handler) = Build(HttpStatusCode.OK);

        await sut.PublishComplexityAsync(
            42, 3.5, 12, 100, 4, 5000, "Analyze", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/pipelines/runs/42/complexity", handler.LastPath);
        Assert.Contains("\"avgCyclomatic\":3.5", handler.LastBody);
        Assert.Contains("\"maxCyclomatic\":12", handler.LastBody);
    }

    [Fact]
    public async Task PublishComplexityAsync_ServerError_Throws()
    {
        var (sut, _) = Build(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.PublishComplexityAsync(
                42, 3.5, 12, 100, 4, 5000, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishCoverageAsync_UsesRawTransferWithoutJsonExpansion()
    {
        var (sut, handler) = Build(HttpStatusCode.OK);
        const string xml = """<coverage lines-covered="49" lines-valid="182"><packages /></coverage>""";

        await sut.PublishCoverageAsync(
            42, xml, "Quality & Audit", "coverage/Cobertura.xml", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            "/api/pipelines/runs/42/coverage/raw?stageName=Quality%20%26%20Audit&stepName=coverage%2FCobertura.xml",
            handler.LastPath);
        Assert.Equal("application/xml", handler.LastContentType);
        Assert.Equal(xml, handler.LastBody);
        Assert.True(handler.LastExpectContinue);
    }

    [Fact]
    public void PublishCoverageAsync_RawNearLimitFitsWhenLegacyJsonWouldNot()
    {
        const string prefix = """<coverage lines-covered="1" lines-valid="1"><packages>""";
        const string fragment = """<class name="x"/>""";
        const string suffix = "</packages></coverage>";
        var limit = CoverageUploadLimits.MaxRawXmlBytes;
        var fixedBytes = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(suffix);
        var fragmentBytes = Encoding.UTF8.GetByteCount(fragment);
        var repetitions = (limit - fixedBytes) / fragmentBytes;
        var rawBytes = fixedBytes + (repetitions * fragmentBytes);
        // Every valid fragment contains two attribute quotes; JSON prefixes each with '\'.
        var legacyJsonBytes = rawBytes + (repetitions * 2L) + """{"xmlContent":""".Length + 2;

        Assert.InRange(rawBytes, limit - fragmentBytes, limit);
        Assert.True(legacyJsonBytes > limit);
        Assert.True(PipelineCoveragePublisher.TryNormalizeCoverage(
            prefix + fragment + suffix, out _, out _, out _));
    }

    [Fact]
    public async Task UploadArtifactAsync_Success_SendsRequest()
    {
        var (sut, handler) = Build(HttpStatusCode.OK);
        using var content = new MemoryStream([1, 2, 3, 4]);

        await sut.UploadArtifactAsync(
            42, "build.zip", "Build", content, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.True(handler.LastExpectContinue);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/artifacts/upload/42?name=build.zip&stageName=Build", handler.LastPath);
        Assert.Equal("application/octet-stream", handler.LastContentType);
        Assert.Equal(4, handler.LastBody!.Length);
    }

    [Fact]
    public async Task PublishAnalysisReportAsync_LargePayload_UsesLongRunningTransferClient()
    {
        var rpcHandler = new DelayedReportHandler(TimeSpan.FromMilliseconds(50));
        var transferHandler = new DelayedReportHandler(TimeSpan.FromMilliseconds(50));
        using var rpcClient = new HttpClient(rpcHandler)
        {
            BaseAddress = new Uri("http://localhost:5301/"),
            Timeout = TimeSpan.FromMilliseconds(1)
        };
        using var transferClient = new HttpClient(transferHandler)
        {
            BaseAddress = new Uri("http://localhost:5301/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("AetheusServer").Returns(rpcClient);
        factory.CreateClient("AetheusServerTransfer").Returns(transferClient);
        var sut = new ServerApiClient(factory, new AgentState(TimeProvider.System));
        var measuredArchitecturePayload = new string('x', 12_902_465);

        var result = await sut.PublishAnalysisReportAsync(
            42,
            new PublishAnalysisReportRequest
            {
                ScannerKey = "dotnet-architecture-metrics",
                ScannerName = "Microsoft.CodeAnalysis Architecture",
                ScannerVersion = "1",
                ReportContent = measuredArchitecturePayload
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result!.Id);
        Assert.Equal(0, rpcHandler.Calls);
        Assert.Equal(1, transferHandler.Calls);
        Assert.True(transferHandler.BodyLength > measuredArchitecturePayload.Length);
    }
}
