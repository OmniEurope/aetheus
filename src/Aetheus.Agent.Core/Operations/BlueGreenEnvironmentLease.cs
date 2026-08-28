// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// An exclusive lease on one blue-green environment, held for the duration of a single operation.
///
/// The shell cutover this replaced held a <c>flock -n</c> across the whole deployment; splitting the
/// cutover into separate steps lost it, and <see cref="BlueGreenJournal.TryOpen"/> alone cannot make
/// up for it: reading the state and then writing it is a check-then-act, so two runs (a webhook and a
/// manual retry, a nightly and an operator) could both read the same idle colour and corrupt the very
/// transaction that refusal exists to protect. <c>bluegreen-up</c> and <c>bluegreen-migrate</c> do not
/// touch the journal at all, so nothing else serialised them.
///
/// The lease is per-operation rather than per-deployment, because the journal - not a held handle - is
/// what carries state across steps. It guarantees that no two blue-green operations mutate the same
/// environment at the same instant, which is what makes the journal's state machine trustworthy.
///
/// <see cref="FileShare.None"/> is the portable form of the same primitive: .NET maps it to an
/// advisory <c>flock</c> on Unix and to a share-mode denial on Windows, so a second holder is refused
/// rather than queued, and the lease is released even if the process dies.
/// </summary>
internal sealed class BlueGreenEnvironmentLease : IDisposable
{
    private readonly FileStream _handle;

    private BlueGreenEnvironmentLease(FileStream handle) => _handle = handle;

    /// <summary>
    /// Takes the lease, or explains why another run holds it. Never waits: a second concurrent
    /// deployment of the same environment is an operator error to report, not a queue to join.
    /// </summary>
    internal static bool TryAcquire(BlueGreenContext context, out BlueGreenEnvironmentLease? lease, out string error)
    {
        lease = null;
        var path = Path.Combine(context.StateDir, "deployment.lock");
        try
        {
            var handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            lease = new BlueGreenEnvironmentLease(handle);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Another blue-green operation is already running against '{context.Project}' "
                + $"(lease {path} is held: {ex.Message}). Wait for it to finish, or resolve it, before retrying.";
            return false;
        }
    }

    public void Dispose() => _handle.Dispose();
}
