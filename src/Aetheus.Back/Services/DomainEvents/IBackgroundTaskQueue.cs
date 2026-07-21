// SPDX-License-Identifier: EUPL-1.2
using System.Threading.Channels;

namespace Aetheus.Back.Services.DomainEvents;

/// <summary>
/// Bounded in-memory queue used by <see cref="IDomainEventDispatcher.Publish{TEvent}"/> to
/// move non-critical event dispatching off the emitting transaction.
/// Items are drained by <see cref="BackgroundTaskQueueHostedService"/>.
/// </summary>
public interface IBackgroundTaskQueue
{
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> work);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);

    /// <summary>Approximate number of items waiting in the queue.</summary>
    int CurrentLength { get; }

    /// <summary>Bounded capacity of the underlying channel.</summary>
    int Capacity { get; }

    /// <summary>Number of items evicted by <c>DropOldest</c> overflow strategy since process start.</summary>
    long Dropped { get; }

    /// <summary>Total items enqueued since process start.</summary>
    long Enqueued { get; }

    /// <summary>Total items processed (or dropped) since process start.</summary>
    long Processed { get; }

    /// <summary>Increment the processed counter. Called by the hosted service after a work item completes.</summary>
    void IncrementProcessed();
}

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private const int CapacityValue = 1024;

    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

    public BackgroundTaskQueue()
    {
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity: CapacityValue)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            }, _ => Interlocked.Increment(ref _dropped));
    }

    private long _enqueued;
    private long _dropped;
    private long _processed;

    public int Capacity => CapacityValue;
    public int CurrentLength => _queue.Reader.Count;
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Processed => Interlocked.Read(ref _processed);

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        // The channel's itemDropped callback owns the exact eviction count. Sampling
        // Reader.Count before TryWrite is racy with the single consumer and over-counts.
        if (!_queue.Writer.TryWrite(work))
            throw new InvalidOperationException("The background task queue rejected a write while open.");
        Interlocked.Increment(ref _enqueued);
    }

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
        => _queue.Reader.ReadAsync(ct);

    public void IncrementProcessed() => Interlocked.Increment(ref _processed);
}
