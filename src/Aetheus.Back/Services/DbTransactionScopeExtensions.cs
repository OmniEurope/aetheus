// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public static class DbTransactionScopeExtensions
{
    /// <summary>
    /// Wraps <paramref name="work"/> in a Begin/Commit/Rollback envelope. On exception, rolls back and rethrows.
    /// </summary>
    public static async Task ExecuteInTransactionAsync(
        this IDbTransactionScope tx, Func<Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(work);

        await tx.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<TResult> ExecuteInTransactionAsync<TResult>(
        this IDbTransactionScope tx, Func<Task<TResult>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(work);

        await tx.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await work().ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
