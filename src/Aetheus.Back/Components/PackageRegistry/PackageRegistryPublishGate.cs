// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;

namespace Aetheus.Back.Components.PackageRegistry;

internal sealed class PackageRegistryPublishGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(
        PackageRegistryKind kind, string normalizedName, CancellationToken ct)
    {
        var key = $"{kind}:{normalizedName}";
        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new GateLease(gate);
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
