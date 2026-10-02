// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;

namespace Aetheus.Back.Components.AppMonitoring.Ingest;

/// <summary>
/// Per-app metric-ingest serialization (singleton). The metric-name cardinality cap reads a snapshot of the
/// app's name set, computes a budget (<c>200 - count</c>) and writes new names back; two concurrent ingests
/// for the same app would both see the same budget and race past the cap (last-writer-wins on the cache). A
/// per-app <see cref="SemaphoreSlim"/> serializes ingestion for a single app so the budget stays accurate,
/// while different apps never contend.
/// </summary>
public sealed class AppIngestGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(int appId) => _locks.GetOrAdd(appId, static _ => new SemaphoreSlim(1, 1));

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _analyticsLocks = new();

    /// <summary>
    /// Recette R-487: the audience ingestion of an app has its own lock. It shared the metrics one, so
    /// a page view waited behind every slow metrics batch of the same app (p95 562 ms in production for
    /// a median of 52 ms). The two protect different things: the metric-name budget there, the route
    /// budget and the storage quota here.
    /// </summary>
    public SemaphoreSlim ForAnalytics(int appId) =>
        _analyticsLocks.GetOrAdd(appId, static _ => new SemaphoreSlim(1, 1));
}
