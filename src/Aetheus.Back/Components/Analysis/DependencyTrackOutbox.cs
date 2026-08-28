// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Analysis;

internal sealed class DependencyTrackOutbox(
    DependencyTrackOutboxRepository repository,
    IOptions<DependencyTrackOptions> options,
    TimeProvider timeProvider) : IDependencyTrackOutbox
{
    private readonly DependencyTrackOptions _options = options.Value;

    public Task EnqueueSbomAsync(int analysisReportId, CancellationToken ct) =>
        !_options.Enabled
            ? Task.CompletedTask
            : repository.EnqueueAsync(analysisReportId, timeProvider.GetUtcNow().UtcDateTime, ct);
}
