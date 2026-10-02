// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Front.Tests;

public class AuthDelegatingHandlerTests
{
    [Fact]
    public async Task SendAsync_WithToken_AddsAuthorizationHeader()
    {
        var (_, handler, client) = CreateSetup("test-token");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_WithoutToken_NoAuthorizationHeader()
    {
        var (_, handler, client) = CreateSetup(null);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Null(request.Headers.Authorization);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401OnApiEndpoint_LogsOut()
    {
        var (authProvider, handler, client) = CreateSetup("token", HttpStatusCode.Unauthorized);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Token should be cleared (LogoutAsync was called)
        Assert.Null(authProvider.Token);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401OnLoginEndpoint_DoesNotLogout()
    {
        var (authProvider, handler, client) = CreateSetup("token", HttpStatusCode.Unauthorized);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/auth/login");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("token", authProvider.Token);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_200_ReturnsResponseNormally()
    {
        var (_, handler, client) = CreateSetup("token", HttpStatusCode.OK);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.Dispose();
        handler.Dispose();
    }

    private static (AuthStateProvider auth, AuthDelegatingHandler handler, HttpClient client) CreateSetup(
        string? token, HttpStatusCode innerStatusCode = HttpStatusCode.OK)
    {
        var jsMock = Substitute.For<Microsoft.JSInterop.IJSRuntime>();
        var authProvider = new AuthStateProvider(jsMock, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthStateProvider>.Instance);

        if (token is not null)
        {
            var prop = typeof(AuthStateProvider).GetProperty("Token")!;
            prop.SetValue(authProvider, token);
        }

        var navMock = new FakeNavigationManager();
        var innerHandler = new StubHandler(innerStatusCode);
        var handler = new AuthDelegatingHandler(authProvider, NullLogger<AuthDelegatingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        var client = new HttpClient(handler);
        return (authProvider, handler, client);
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            // No-op for testing
        }
    }

    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
