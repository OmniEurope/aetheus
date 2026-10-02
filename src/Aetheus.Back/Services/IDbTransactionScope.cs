// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public interface IDbTransactionScope
{
    /// <summary>
    /// True when the underlying provider supports real transactions (PostgreSQL). False for the
    /// InMemory unit-test provider, which has no transaction support - callers that need an
    /// IsRelational guard (matching the repository-layer pattern) can branch on this.
    /// </summary>
    bool IsRelational { get; }

    /// <summary>
    /// True while a transaction of this scope is open (or one opened on its connection is joined): a
    /// write made now is undone if the caller rolls back.
    /// </summary>
    bool InTransaction { get; }

    /// <summary>
    /// Opens the request's transaction, or joins the one already open on this scope. Re-entrant: two
    /// services that each wrap their own work compose without EF refusing a nested begin. The
    /// outermost <see cref="CommitAsync"/> is the one that commits.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes one level. Commits only at the outermost one, and throws there when an inner level
    /// rolled back - the work is already abandoned, so committing would persist half of it.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls back. At an inner level it marks the whole nesting as doomed rather than undoing the
    /// outer caller's work behind its back; the outermost commit then refuses and everything is undone.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
