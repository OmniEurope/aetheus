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

    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
