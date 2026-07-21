// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Locks down the contract the installer relies on: every failure mode must
/// resolve to <c>Ok = false</c> with a short human message, well within the
/// installer's expectations. We deliberately avoid hitting the network in
/// tests - the timeout-only branch and the URL-validation branch are enough
/// to keep the protocol stable, and a real HTTP call would make CI flaky.
/// </summary>
public class BackendProbeTests
{
    [Fact]
    public async Task ProbeAsync_EmptyUrl_ReturnsClearMessage()
    {
        var result = await BackendProbe.ProbeAsync(
            string.Empty, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
        Assert.Contains("ServerUrl is empty", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_MalformedUrl_ReturnsClearMessage()
    {
        var result = await BackendProbe.ProbeAsync(
            "not-a-url", TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
        Assert.Contains("not a valid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // S-TECH-F2J7: drive the unreachable branch through a fake handler instead of a real call to
    // TEST-NET-1 - the old version made a live network attempt and was flaky under parallel load.
    [Fact]
    public async Task ProbeAsync_UnreachableHost_ReturnsClearMessage()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused"));

        var result = await BackendProbe.ProbeAsync(
            "https://192.0.2.1", TimeSpan.FromSeconds(2), handler,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("unreachable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Deterministic timeout branch: the handler honours the client's timeout token and is cancelled,
    // so HttpClient surfaces TaskCanceledException → the "did not respond" message. No real wall-clock
    // wait beyond the tiny timeout.
    [Fact]
    public async Task ProbeAsync_Timeout_ReturnsTimedOutMessage()
    {
        var handler = new StubHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        var result = await BackendProbe.ProbeAsync(
            "https://example.com", TimeSpan.FromMilliseconds(50), handler,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("did not respond", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_Healthy_ReturnsOk()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));

        var result = await BackendProbe.ProbeAsync(
            "https://example.com", TimeSpan.FromSeconds(2), handler,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(200, result.HttpStatus);
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(cancellationToken);
    }
}
