// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Core.Tests;

public class BearerTokenHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsAuthorizationHeader_WhenTokenExists()
    {
        var state = new AgentState { BearerToken = "test-token-123" };
        var handler = new BearerTokenHandler(state)
        {
            InnerHandler = new FakeHandler()
        };
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-token-123", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_DoesNotAddHeader_WhenTokenIsNull()
    {
        var state = new AgentState { BearerToken = null };
        var handler = new BearerTokenHandler(state)
        {
            InnerHandler = new FakeHandler()
        };
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_DoesNotAddHeader_WhenTokenIsEmpty()
    {
        var state = new AgentState { BearerToken = "" };
        var handler = new BearerTokenHandler(state)
        {
            InnerHandler = new FakeHandler()
        };
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(request.Headers.Authorization);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
