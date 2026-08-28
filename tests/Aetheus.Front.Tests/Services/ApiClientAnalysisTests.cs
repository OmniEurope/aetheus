// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests.Services;

public sealed class ApiClientAnalysisTests
{
    [Fact]
    public async Task GetAnalysisRunGateAsync_UsesUserFacingResultEndpoint()
    {
        var handler = new RecordingResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AnalysisRunGateDto { PipelineRunId = 42 })
        });
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

        var result = await api.Analysis.GetAnalysisRunGateAsync(42, TestContext.Current.CancellationToken);

        Assert.Equal(42, result?.PipelineRunId);
        Assert.Equal("http://test/api/analysis/runs/42/result", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetAnalysisTrackingStatusAsync_NoContent_ReturnsNull()
    {
        var api = Create(new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await api.Analysis.GetAnalysisTrackingStatusAsync(42, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAnalysisTrackingStatusAsync_Ok_ReturnsStatus()
    {
        var expected = new AnalysisTrackingStatusDto
        {
            ProjectId = 42,
            Provider = "DependencyTrack",
            Active = true,
            SyncStatus = "Succeeded"
        };
        var api = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });

        var result = await api.Analysis.GetAnalysisTrackingStatusAsync(42, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetAnalysisTrackingStatusAsync_Failure_DoesNotHideHttpError()
    {
        var api = Create(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            api.Analysis.GetAnalysisTrackingStatusAsync(42, TestContext.Current.CancellationToken));
    }

    private static ApiClient Create(HttpResponseMessage response)
    {
        var handler = new StaticResponseHandler(response);
        return new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }

    private sealed class RecordingResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(response);
        }
    }
}
