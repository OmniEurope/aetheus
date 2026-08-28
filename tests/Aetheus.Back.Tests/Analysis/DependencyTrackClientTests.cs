// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text;
using Aetheus.Back.Components.Analysis;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Tests.Analysis;

public sealed class DependencyTrackClientTests
{
    [Fact]
    public async Task GetVulnerabilitiesAsync_ParsesBoundedResponse()
    {
        var client = Create("""
            [{"vulnerability":{"vulnId":"CVE-2026-1","severity":"HIGH"},
              "component":{"name":"demo","version":"1.0","purl":"pkg:nuget/demo@1.0"},
              "analysis":{"state":"NOT_SET"}}]
            """);

        var result = await client.GetVulnerabilitiesAsync("project", TestContext.Current.CancellationToken);

        var finding = Assert.Single(result);
        Assert.Equal("CVE-2026-1", finding.VulnerabilityId);
        Assert.Equal(AnalysisSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task GetVulnerabilitiesAsync_RejectsExcessFindingCountInsteadOfTruncating()
    {
        var client = Create("[{},{}]", new DependencyTrackOptions { MaxFindings = 1 });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetVulnerabilitiesAsync("project", TestContext.Current.CancellationToken));

        Assert.Contains("more than", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVulnerabilitiesAsync_RejectsDeclaredOversizedResponseBeforeReading()
    {
        var content = new StringContent("[]", Encoding.UTF8, "application/json");
        content.Headers.ContentLength = 2_000_000;
        var client = Create(content, new DependencyTrackOptions { MaxResponseBytes = 1_048_576 });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetVulnerabilitiesAsync("project", TestContext.Current.CancellationToken));

        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DependencyTrackClient Create(string content, DependencyTrackOptions? options = null) =>
        Create(new StringContent(content, Encoding.UTF8, "application/json"), options);

    private static DependencyTrackClient Create(HttpContent content, DependencyTrackOptions? options = null)
    {
        var handler = new StubHandler(content);
        return new DependencyTrackClient(new HttpClient(handler) { BaseAddress = new Uri("https://dependency-track.test/") },
            Options.Create(options ?? new DependencyTrackOptions()), TimeProvider.System);
    }

    private sealed class StubHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
