// SPDX-License-Identifier: EUPL-1.2
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// The liveness probe decides whether the connection-lost overlay may close. The API client's
/// resilience pipeline reports its own timeout and an open circuit as Polly rejections, not as the
/// HttpRequestException the probe caught: the probe then threw inside the layout's fire-and-forget
/// refresh, and the overlay kept the answer of the last probe that had returned - "offline".
/// </summary>
public class BackendLivenessProbeTests
{
    public static TheoryData<Exception> Rejections => new()
    {
        new TimeoutRejectedException("attempt timed out"),
        new BrokenCircuitException("circuit is open"),
        new HttpRequestException("connection refused"),
        new TaskCanceledException("request cancelled"),
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task IsBackendLiveAsync_TransportRejection_AnswersNotLiveInsteadOfThrowing(Exception rejection)
    {
        using var client = new HttpClient(new ThrowingHandler(rejection)) { BaseAddress = new Uri("http://localhost/") };
        var api = new AuthApi(client);

        var live = await api.IsBackendLiveAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.False(live);
    }

    private sealed class ThrowingHandler(Exception rejection) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(rejection);
    }
}
