// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using Aetheus.Back.Components.Shared;

namespace Aetheus.Back.Tests.SharedComponents;

/// <summary>
/// A360-02. The factory that builds every outbound HTTP client allowed to reach a user-supplied URL was
/// at 0 % coverage. Its whole reason to exist is the <c>ConnectCallback</c>: the SSRF decision is taken
/// at CONNECT time, on the address actually resolved, which is what closes the DNS-rebinding window a
/// pre-flight hostname check leaves open.
///
/// These drive the real handler over loopback rather than asserting that a callback was assigned:
/// loopback IS a forbidden address, so a request to it must be refused with the caller's own message,
/// and must be allowed when the caller opted into private targets.
/// </summary>
public sealed class SsrfProtectedHttpHandlerFactoryTests
{
    private const string Refusal = "This target is not allowed.";

    [Fact]
    public async Task AForbiddenAddress_IsRefusedAtConnectTime_WithTheCallersMessage()
    {
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: false, Refusal);
        using var client = new HttpClient(handler);

        // Port 9 (discard) on loopback: the point is that the refusal happens before any connection is
        // attempted, so nothing needs to be listening.
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("http://127.0.0.1:9/"), TestContext.Current.CancellationToken));

        Assert.Contains(Refusal, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AForbiddenAddressReachedByHostname_IsAlsoRefused()
    {
        // The check runs on the RESOLVED address, so a name that resolves to loopback is refused just
        // like the literal. This is the case a hostname allowlist would miss.
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: false, Refusal);
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("http://localhost:9/"), TestContext.Current.CancellationToken));

        Assert.Contains(Refusal, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenPrivateTargetsAreAllowed_TheSameAddressIsNotRefused()
    {
        // Proves the refusal above comes from the guard and not from the connection failing: with
        // allowPrivate the guard admits the address, so the request proceeds to a genuine connection
        // error instead of the caller's refusal message.
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: true, Refusal);
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("http://127.0.0.1:9/"), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Refusal, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHandler_DoesNotFollowRedirects_SoARedirectCannotEscapeTheGuard()
    {
        // A followed redirect would connect a second time, to an address the caller never vetted.
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: false, Refusal);

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void TheHandler_DoesNotSendCookies()
    {
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: false, Refusal);

        Assert.False(handler.UseCookies);
    }

    [Fact]
    public void TheHandler_InstallsAConnectCallback_WhichIsWhereTheDecisionHasToLive()
    {
        using var handler = SsrfProtectedHttpHandlerFactory.Create(allowPrivate: false, Refusal);

        Assert.NotNull(handler.ConnectCallback);
    }
}
