// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal sealed class ScannerSourceProjectionLease(
    string sourceDirectory,
    Func<ValueTask>? release) : IAsyncDisposable
{
    private Func<ValueTask>? _release = release;
    public string SourceDirectory { get; } = sourceDirectory;

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _release, null)?.Invoke() ?? ValueTask.CompletedTask;
}
