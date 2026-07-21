// SPDX-License-Identifier: EUPL-1.2
using System.Net;
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

    private static (ServerApiClient Sut, StatusHandler Handler) Build(HttpStatusCode status)
    {
        var handler = new StatusHandler(status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5301/") };
        var factory = Substitute.For<IHttpClientFactory>();
        // Both named clients (RPC "AetheusServer" + streamed "AetheusServerTransfer")
        // resolve to the same handler here - the split only matters for resilience/timeout.
        factory.CreateClient(Arg.Any<string>()).Returns(http);
        return (new ServerApiClient(factory), handler);
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
}
