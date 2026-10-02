// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.SystemLogs;

/// <summary>
/// Recette R-453: the columns of the system log grid. The grid reads the log block by block from the
/// API (no more "Load more"), so its header filters and its sort are applied here, to every entry the
/// bar filters kept, and not to the block already on screen. A request only names keys; the column
/// behind each key is decided here.
/// </summary>
internal static class SystemLogQuery
{
    internal static readonly GridQueryMap<SystemLogEntryDto> Columns = new GridQueryMap<SystemLogEntryDto>()
        .Date("timestamp", entry => entry.Timestamp)
        .Text("level", entry => entry.Level)
        .Text("category", entry => entry.Category)
        .Text("message", entry => entry.Message)
        .Text("correlationId", entry => entry.CorrelationId);

    /// <summary>The single sort a request carries, or none when it names no column.</summary>
    internal static IReadOnlyList<GridSort>? SortsOf(string? sortBy, bool sortDescending) =>
        string.IsNullOrWhiteSpace(sortBy)
            ? null
            : [new GridSort { Field = sortBy.Trim(), Descending = sortDescending }];
}
