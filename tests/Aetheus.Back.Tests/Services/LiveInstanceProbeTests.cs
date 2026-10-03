// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text;
using Aetheus.Back.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests.Services;

/// <summary>
/// Only the colour behind the public API address leads (decision of 2026-10-02). The probe is the
/// fact that decision rests on: it must say Standby only when the address positively names another
/// instance, and fall back to Unknown (today's behaviour) on anything it cannot read.
/// </summary>
public sealed class LiveInstanceProbeTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(answer(request));
        }
    }

    private static (LiveInstanceProbe Probe, StubHandler Handler) Create(
        Func<LiveInstanceProbe, HttpResponseMessage> answer, string? publicApiUrl = "https://api.example.test/")
    {
        LiveInstanceProbe? probe = null;
        var handler = new StubHandler(_ => answer(probe!));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(LiveInstanceProbe.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Aetheus:PublicApiBaseUrl"] = publicApiUrl })
            .Build();
        probe = new LiveInstanceProbe(configuration, factory, NullLogger<LiveInstanceProbe>.Instance);
        return (probe, handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task TheAddressAnsweringWithThisInstance_IsLive()
    {
        var (probe, handler) = Create(self => Json($$"""{"instanceId":"{{self.InstanceId}}"}"""));

        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstanceServingState.Live, probe.State);
        Assert.Equal(new Uri("https://api.example.test/health/instance"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task TheAddressAnsweringWithAnotherInstance_IsStandby()
    {
        var (probe, _) = Create(_ => Json("""{"instanceId":"another-colour"}"""));

        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstanceServingState.Standby, probe.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task AnUnreadableAnswer_IsUnknown_NeverStandby(HttpStatusCode status, string body)
    {
        var (probe, _) = Create(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstanceServingState.Unknown, probe.State);
    }

    [Fact]
    public async Task WithoutAPublicAddress_NothingIsAsked_AndTheStateStaysUnknown()
    {
        var (probe, handler) = Create(_ => Json("""{"instanceId":"x"}"""), publicApiUrl: null);

        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests);
        Assert.Equal(InstanceServingState.Unknown, probe.State);
    }

    [Fact]
    public async Task AReserveThatTakesTrafficBack_BecomesLiveAgain()
    {
        var answerOther = true;
        var (probe, _) = Create(self => Json($$"""{"instanceId":"{{(answerOther ? "other" : self.InstanceId)}}"}"""));

        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstanceServingState.Standby, probe.State);

        answerOther = false; // a watchdog revert stopped the new colour
        await probe.ProbeOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstanceServingState.Live, probe.State);
    }
}
