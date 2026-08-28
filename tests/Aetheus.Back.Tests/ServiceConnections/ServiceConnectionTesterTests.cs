// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Back.Components.ServiceConnections;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServiceConnectionTesterTests
{
    private static ServiceConnectionTester Build(HttpStatusCode? stubStatus)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        if (stubStatus is not null)
        {
            var client = new HttpClient(new StubHandler(stubStatus.Value));
            factory.CreateClient("service-connection-test").Returns(client);
        }
        return new ServiceConnectionTester(factory, NullLogger<ServiceConnectionTester>.Instance);
    }

    [Theory]
    [InlineData(ServiceConnectionType.SSH)]
    [InlineData(ServiceConnectionType.Kubernetes)]
    [InlineData(ServiceConnectionType.Generic)]
    public async Task Test_UnprobeableProvider_ReturnsUnsupported_NotGreen(ServiceConnectionType type)
    {
        var sut = Build(stubStatus: null);

        var result = await sut.TestAsync(type, "https://example.com", "{}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task Test_GitHubWithoutToken_ReturnsInvalid_NoNetworkCall()
    {
        var sut = Build(stubStatus: null); // no client configured: proves no HTTP is attempted

        var result = await sut.TestAsync(ServiceConnectionType.GitHub, "https://github.com", "{}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Test_GitHubWithToken_Ok_ReturnsValid()
    {
        var sut = Build(HttpStatusCode.OK);

        var result = await sut.TestAsync(ServiceConnectionType.GitHub, "https://github.com", "{\"token\":\"t\"}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Valid, result.Status);
    }

    [Fact]
    public async Task Test_GitLabWithToken_Unauthorized_ReturnsInvalid()
    {
        var sut = Build(HttpStatusCode.Unauthorized);

        var result = await sut.TestAsync(ServiceConnectionType.GitLab, "https://gitlab.com", "{\"token\":\"bad\"}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Test_HttpUrl_RejectedAsInsecure_NoNetworkCall()
    {
        var sut = Build(stubStatus: null); // no client configured: proves the rejection precedes any HTTP call

        var result = await sut.TestAsync(
            ServiceConnectionType.DockerRegistry, "http://registry.internal", "{\"username\":\"u\",\"password\":\"p\"}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Invalid, result.Status);
        Assert.Contains("https", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_DockerBearerChallenge_ExchangesTokenAndRetriesRegistry()
    {
        var handler = new DockerBearerChallengeHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("service-connection-test").Returns(new HttpClient(handler));
        var sut = new ServiceConnectionTester(factory, NullLogger<ServiceConnectionTester>.Instance);

        var result = await sut.TestAsync(
            ServiceConnectionType.DockerRegistry,
            "https://registry.example",
            "{\"username\":\"alice\",\"password\":\"secret\"}",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Valid, result.Status);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("Basic", handler.Requests[0].Authorization?.Scheme);
        Assert.Equal("https://auth.example/token?service=registry.example&scope=registry%3Acatalog%3A%2A",
            handler.Requests[1].Uri);
        Assert.Equal("Basic", handler.Requests[1].Authorization?.Scheme);
        Assert.Equal("Bearer", handler.Requests[2].Authorization?.Scheme);
        Assert.Equal("issued-token", handler.Requests[2].Authorization?.Parameter);
    }

    [Fact]
    public async Task Test_DockerBearerChallenge_WhenTokenServiceRejects_ReturnsInvalid()
    {
        var handler = new DockerBearerChallengeHandler(tokenStatus: HttpStatusCode.Unauthorized);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("service-connection-test").Returns(new HttpClient(handler));
        var sut = new ServiceConnectionTester(factory, NullLogger<ServiceConnectionTester>.Instance);

        var result = await sut.TestAsync(
            ServiceConnectionType.DockerRegistry,
            "https://registry.example",
            "{\"username\":\"alice\",\"password\":\"wrong\"}",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Invalid, result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Test_TransportFailure_ReturnsError_NotGreen()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("service-connection-test")
            .Returns(new HttpClient(new ThrowingHandler()));
        var sut = new ServiceConnectionTester(factory, NullLogger<ServiceConnectionTester>.Instance);

        var result = await sut.TestAsync(ServiceConnectionType.GitHub, "https://github.com", "{\"token\":\"t\"}", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceConnectionTestStatus.Error, result.Status);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connect failed");
    }

    private sealed class DockerBearerChallengeHandler(
        HttpStatusCode tokenStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization));

            if (Requests.Count == 1)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                challenge.Headers.WwwAuthenticate.ParseAdd(
                    "Bearer realm=\"https://auth.example/token\",service=\"registry.example\",scope=\"registry:catalog:*\"");
                return Task.FromResult(challenge);
            }

            if (Requests.Count == 2)
            {
                return Task.FromResult(new HttpResponseMessage(tokenStatus)
                {
                    Content = new StringContent(
                        tokenStatus == HttpStatusCode.OK ? "{\"token\":\"issued-token\"}" : "{}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed record CapturedRequest(string Uri, System.Net.Http.Headers.AuthenticationHeaderValue? Authorization);
}
