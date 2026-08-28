// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.WebAnalytics.Tests;

internal sealed class AnalyticsHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
