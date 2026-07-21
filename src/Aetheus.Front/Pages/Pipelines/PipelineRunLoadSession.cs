// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>Owns cancellation and generation tracking for route-scoped pipeline run loads.</summary>
internal sealed class PipelineRunLoadSession : IDisposable
{
    private CancellationTokenSource? _current;
    private int _generation;

    public CancellationToken Token => _current?.Token ?? default;

    public (int Generation, CancellationToken Token) Begin()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return (++_generation, _current.Token);
    }

    public bool IsCurrent(int generation) => generation == _generation;

    public void Dispose()
    {
        _current?.Cancel();
        _current?.Dispose();
    }
}
