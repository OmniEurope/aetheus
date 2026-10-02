// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Dev;

/// <summary>
/// Closed while <c>/api/dev/reset-db</c> drops and rebuilds the E2E schema. Every database command
/// issued outside the reset waits for it to reopen (see <see cref="SchemaResetInterceptor"/>), so the
/// background services polling the database meet the rebuilt schema instead of a half-migrated one
/// (42P01, relation does not exist). Resets are serialised.
/// </summary>
public sealed class SchemaResetGate
{
    private readonly SemaphoreSlim _resets = new(1, 1);
    private readonly AsyncLocal<bool> _insideReset = new();
    private volatile TaskCompletionSource _open = Opened();

    /// <summary>Runs <paramref name="reset"/> with the gate closed to every other flow.</summary>
    public async Task RunClosedAsync(Func<Task> reset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reset);
        await _resets.WaitAsync(ct).ConfigureAwait(false);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _open = closed;
        try
        {
            // Set in this async flow only: the reset's own commands, and whatever it awaits, pass.
            _insideReset.Value = true;
            await reset().ConfigureAwait(false);
        }
        finally
        {
            _insideReset.Value = false;
            _open = Opened();
            closed.SetResult();
            _resets.Release();
        }
    }

    /// <summary>Completes at once when the gate is open or when called from the reset itself.</summary>
    public Task WaitUntilOpenAsync(CancellationToken ct) =>
        _insideReset.Value ? Task.CompletedTask : _open.Task.WaitAsync(ct);

    private static TaskCompletionSource Opened()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }
}
