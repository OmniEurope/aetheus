// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.PackageRegistry;

public sealed class PackageRegistryUploadGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IDisposable? TryEnter() =>
        _gate.Wait(0) ? new GateLease(_gate) : null;

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
