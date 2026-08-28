// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Headers;

namespace Aetheus.Agent.Core.Services;

public sealed class BearerTokenHandler(AgentState agentState) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(agentState.BearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentState.BearerToken);

        return base.SendAsync(request, cancellationToken);
    }
}
