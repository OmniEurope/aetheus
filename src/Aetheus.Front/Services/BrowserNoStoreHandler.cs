// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Aetheus.Front.Services;

/// <summary>
/// Keeps dynamic API reads out of the browser HTTP cache. SignalR tells a page when to reload;
/// allowing the following GET to reuse an older browser response defeats that realtime contract.
/// The WebAssembly fetch option is used instead of a Cache-Control request header so cross-origin
/// development calls do not require an extra CORS-allowed header.
/// </summary>
public sealed class BrowserNoStoreHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);

        return base.SendAsync(request, cancellationToken);
    }
}
