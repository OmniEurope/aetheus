// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;

namespace Aetheus.Back.Components.PackageFeeds;

/// <summary>
/// Per-feed serialization gate (singleton, shared across scopes). The periodic hosted sync and a manual
/// "sync now" from the controller run in separate DI scopes, so without a shared lock they can sync the
/// same feed concurrently: two outbound registry fan-outs and a last-write-wins race on the tracked
/// versions. A per-feed <see cref="SemaphoreSlim"/> lets a caller skip (rather than duplicate) a sync that
/// is already running for that feed.
/// </summary>
public sealed class PackageFeedSyncGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    private SemaphoreSlim For(int feedId) => _locks.GetOrAdd(feedId, static _ => new SemaphoreSlim(1, 1));

    /// <summary>Tries to enter the gate for <paramref name="feedId"/> without waiting. Returns true when the
    /// caller now owns the sync (must <see cref="Release"/> it), false when a sync is already in progress.</summary>
    public Task<bool> TryEnterAsync(int feedId, CancellationToken ct) => For(feedId).WaitAsync(0, ct);

    public void Release(int feedId)
    {
        if (_locks.TryGetValue(feedId, out var sem))
            sem.Release();
    }
}
