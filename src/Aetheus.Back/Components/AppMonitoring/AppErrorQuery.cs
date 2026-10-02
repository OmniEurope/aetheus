// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// The columns of an app's error-group grid. The grid reads the groups page by page from the API, so
/// its header filters and its sort are applied here, to every stored group of the app, and not to the
/// rows already on screen (the defect the log grid had before recette R-358). A request only names
/// keys; the column behind each key is decided here.
/// </summary>
internal static class AppErrorQuery
{
    /// <summary>Most exception types the type column's checkable list offers.</summary>
    internal const int MaximumExceptionTypes = 500;

    /// <summary>The header filters: exception type (checkable list), message text, count, first and last seen.</summary>
    internal static readonly GridQueryMap<AppErrorEvent> Filters = Columns();

    /// <summary>The sortable columns, the same keys as the filters.</summary>
    internal static readonly GridQueryMap<AppErrorEvent> Sorts = Columns();

    /// <summary>The single sort a request carries, or none when it names no column.</summary>
    internal static IReadOnlyList<GridSort>? SortsOf(string? sortBy, bool sortDescending) =>
        AppLogQuery.SortsOf(sortBy, sortDescending);

    private static GridQueryMap<AppErrorEvent> Columns() => new GridQueryMap<AppErrorEvent>()
        .Text("exceptionType", e => e.ExceptionType)
        .Text("message", e => e.Message)
        .Number("occurrenceCount", e => e.OccurrenceCount)
        .Date("firstSeenAt", e => e.FirstSeenAt)
        .Date("lastSeenAt", e => e.LastSeenAt);
}
