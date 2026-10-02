// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Storage;

namespace Aetheus.Back.Services;

/// <summary>
/// The scoped transaction of one request. Re-entrant: the OUTERMOST caller owns the database
/// transaction, and an inner caller that also wraps its work joins it instead of opening a second one.
///
/// Re-entrance is not a convenience. Two services that each legitimately wrap their own write are
/// composed all the time - one calls the other - and they share this scope and its
/// <see cref="AppDbContext"/>. Without it EF throws "the connection is already in a transaction", so
/// the composition fails on PostgreSQL while passing on the InMemory provider that has no
/// transactions at all: a defect no unit test can see. That is exactly how the port allocation of
/// PLAN-005 lot 5 shipped broken.
///
/// An inner rollback dooms the whole nesting: the work is already abandoned, and letting the outer
/// commit succeed would persist half of it. The outer commit then refuses rather than lying.
/// </summary>
public sealed class DbTransactionScope(AppDbContext db) : IDbTransactionScope, IDisposable, IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    /// <summary>How many callers are inside the envelope. Only the first one owns the transaction.</summary>
    private int _depth;

    /// <summary>An inner caller rolled back, so nothing in this nesting may be committed.</summary>
    private bool _doomed;

    public bool IsRelational => db.Database.IsRelational();

    public bool InTransaction => _depth > 0 || (IsRelational && db.Database.CurrentTransaction is not null);

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_depth++ > 0) return;

        _doomed = false;
        // A transaction opened outside this scope (a repository using db.Database directly) is joined
        // the same way: opening a second one on that connection is what EF refuses.
        if (db.Database.CurrentTransaction is null)
            _transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_depth == 0) return;
        if (--_depth > 0) return;

        if (_doomed)
        {
            // Reached when an inner scope rolled back and its caller swallowed the failure. Committing
            // would persist the half the inner one abandoned, so the whole envelope is undone and the
            // caller is told, rather than left believing its write landed.
            await RollbackCoreAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                "An inner transaction scope rolled back, so this transaction cannot be committed.");
        }

        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct).ConfigureAwait(false);
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_depth == 0) return;

        if (--_depth > 0)
        {
            // An inner rollback cannot undo the outer work on its own; it marks the nesting so the
            // outermost commit refuses and everything is undone together.
            _doomed = true;
            return;
        }

        await RollbackCoreAsync(ct).ConfigureAwait(false);
    }

    private async Task RollbackCoreAsync(CancellationToken ct)
    {
        _doomed = false;
        if (_transaction is null)
        {
            // Nothing owned here (InMemory, or a transaction owned elsewhere): still drop the tracked
            // graph, since the caller's work must not survive its own rollback.
            db.ChangeTracker.Clear();
            return;
        }

        await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
        db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }
}
