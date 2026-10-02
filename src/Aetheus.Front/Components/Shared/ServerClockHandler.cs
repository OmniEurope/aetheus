// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Feeds <see cref="ServerClock"/> from the <c>Date</c> header every API response already carries,
/// so nothing extra is fetched and no endpoint has to exist for it.
/// </summary>
public sealed class ServerClockHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Headers.Date is { } serverDate)
            ServerClock.Observe(serverDate, Stopwatch.GetElapsedTime(started));
        return response;
    }
}
