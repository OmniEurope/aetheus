// SPDX-License-Identifier: EUPL-1.2
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aetheus.Back.Components.Dev;

/// <summary>
/// Holds every EF command back while <see cref="SchemaResetGate"/> is closed. Registered in
/// Development only, the one environment where <c>/api/dev/reset-db</c> can close it.
/// </summary>
internal sealed class SchemaResetInterceptor(SchemaResetGate gate) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        gate.WaitUntilOpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        gate.WaitUntilOpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        gate.WaitUntilOpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitUntilOpenAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitUntilOpenAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitUntilOpenAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
