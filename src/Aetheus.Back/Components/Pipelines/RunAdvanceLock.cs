// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Refcounted keyed async lock serializing per-run stage advancement.
/// The previous ConcurrentDictionary + TryRemove-in-finally pattern could evict a
/// semaphore while another caller was still waiting on it, while a third caller got a
/// fresh one - two AdvanceStage passes then ran concurrently for the same run (double
/// task dispatch). Here an entry is only evicted when its last holder/waiter releases
/// it, and the semaphore is disposed at that point (they never were before).
/// </summary>
internal sealed class RunAdvanceLock
{
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly object _gate = new();

    internal sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    public async Task<Releaser> AcquireAsync(int runId, CancellationToken ct)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(runId, out entry!))
            {
                entry = new Entry();
                _entries[runId] = entry;
            }
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ReleaseRef(runId, entry);
            throw;
        }
        return new Releaser(this, runId, entry);
    }

    private void ReleaseRef(int runId, Entry entry)
    {
        lock (_gate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && _entries.TryGetValue(runId, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(runId);
                entry.Semaphore.Dispose();
            }
        }
    }

    internal readonly struct Releaser(RunAdvanceLock owner, int runId, Entry entry) : IDisposable
    {
        public void Dispose()
        {
            entry.Semaphore.Release();
            owner.ReleaseRef(runId, entry);
        }
    }
}
