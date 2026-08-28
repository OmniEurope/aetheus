// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Maintains one immutable Docker-visible source projection per pipeline run/source pair. Concurrent
/// scanners share the exact same bytes. Once the last lease is released, the projection becomes
/// stale: a later scanner must restage the mutable pipeline workspace before reading it.
/// </summary>
public sealed class ScannerSourceProjectionManager
{
    private static readonly TimeSpan DefaultCleanupDelay = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, ProjectionState> _projections = new(StringComparer.Ordinal);
    private readonly TimeSpan _cleanupDelay;
    private int _stagingCount;

    public ScannerSourceProjectionManager() : this(DefaultCleanupDelay) { }

    internal ScannerSourceProjectionManager(TimeSpan cleanupDelay)
    {
        _cleanupDelay = cleanupDelay;
    }

    internal int StagingCount => Volatile.Read(ref _stagingCount);
    internal int ActiveProjectionCount => _projections.Count;

    internal async Task<ScannerSourceProjectionLease> AcquireAsync(
        int runId,
        string sourceDirectory,
        string workDirectory,
        bool includeLockedDependencies,
        CancellationToken ct)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var workRoot = Path.GetFullPath(workDirectory);
        if (ScannerSourceStager.IsPathWithin(sourceRoot, workRoot))
            return new ScannerSourceProjectionLease(sourceRoot, null);

        var comparisonSource = OperatingSystem.IsWindows()
            ? sourceRoot.ToUpperInvariant()
            : sourceRoot;
        var key = $"{runId}:{comparisonSource}:{includeLockedDependencies}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var destination = Path.Combine(workRoot, "scanner-projections", $"{runId}-{hash}");
        while (true)
        {
            var state = _projections.GetOrAdd(key, _ => new ProjectionState(destination));
            await state.Gate.WaitAsync(ct).ConfigureAwait(false);
            if (!_projections.TryGetValue(key, out var current) || !ReferenceEquals(current, state))
            {
                state.Gate.Release();
                continue;
            }
            try
            {
                state.CleanupCancellation?.Cancel();
                state.CleanupCancellation?.Dispose();
                state.CleanupCancellation = null;
                if (!state.Ready || state.NeedsRefresh)
                {
                    if (Directory.Exists(destination))
                        Directory.Delete(destination, recursive: true);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        // Copies, rather than hard links, make the projection independent from later
                        // checkout writes while all scanner containers mount it read-only.
                        ScannerSourceStager.StageSourceTree(
                            sourceRoot,
                            temporary,
                            useHardLinks: false,
                            includeNodeModules: includeLockedDependencies);
                        Directory.Move(temporary, destination);
                    }
                    finally
                    {
                        if (Directory.Exists(temporary))
                            Directory.Delete(temporary, recursive: true);
                    }
                    state.Ready = true;
                    state.NeedsRefresh = false;
                    Interlocked.Increment(ref _stagingCount);
                }
                state.ReferenceCount++;
                return new ScannerSourceProjectionLease(
                    destination,
                    () => ReleaseAsync(key, state));
            }
            catch
            {
                if (!state.Ready)
                    _projections.TryRemove(new KeyValuePair<string, ProjectionState>(key, state));
                throw;
            }
            finally
            {
                state.Gate.Release();
            }
        }
    }

    private async ValueTask ReleaseAsync(string key, ProjectionState state)
    {
        await state.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (--state.ReferenceCount > 0)
                return;
            // Pipeline stages may mutate the workspace between scanner waves (for example by
            // restoring and preparing an image archive). The idle grace is only a cleanup delay;
            // it must never make a later scanner reuse an earlier stage's snapshot.
            state.NeedsRefresh = true;
            var cleanup = new CancellationTokenSource();
            state.CleanupCancellation = cleanup;
            _ = CleanupAfterIdleAsync(key, state, cleanup);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task CleanupAfterIdleAsync(
        string key,
        ProjectionState state,
        CancellationTokenSource cleanup)
    {
        try
        {
            if (_cleanupDelay > TimeSpan.Zero)
                await Task.Delay(_cleanupDelay, cleanup.Token).ConfigureAwait(false);
            await state.Gate.WaitAsync(cleanup.Token).ConfigureAwait(false);
            try
            {
                if (state.ReferenceCount != 0 || !ReferenceEquals(state.CleanupCancellation, cleanup))
                    return;
                if (Directory.Exists(state.Destination))
                    Directory.Delete(state.Destination, recursive: true);
                state.Ready = false;
                state.CleanupCancellation = null;
                _projections.TryRemove(new KeyValuePair<string, ProjectionState>(key, state));
            }
            finally
            {
                state.Gate.Release();
            }
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { }
        finally
        {
            cleanup.Dispose();
        }
    }

    private sealed class ProjectionState(string destination)
    {
        public string Destination { get; } = destination;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
        public bool Ready { get; set; }
        public bool NeedsRefresh { get; set; }
        public CancellationTokenSource? CleanupCancellation { get; set; }
    }
}
