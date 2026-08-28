// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.WebAnalytics.Tests;

internal sealed class AnalyticsExportRecordingHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource<(string Body, string? IngestKey)> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<(string Body, string? IngestKey)> Received => _received.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        _received.TrySetResult((
            body,
            request.Headers.TryGetValues("x-aetheus-ingest-key", out var values)
                ? values.SingleOrDefault()
                : null));
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
}
