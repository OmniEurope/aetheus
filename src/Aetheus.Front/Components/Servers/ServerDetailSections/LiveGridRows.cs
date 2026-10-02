// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>
/// Recette R-227: a grid fed through <c>Items</c> whose rows a live event replaces (a server heartbeat,
/// a task completion). A section calls <see cref="ObserveAsync{TItem}"/> from its parameter update with
/// the list that event brought; when that list is a new one, the grid is told before it receives it,
/// so the rows it did not hold read bold for its <c>NewRowHighlight</c>. The first list seen for a
/// scope (the server shown) is its initial load and marks nothing.
/// </summary>
internal sealed class LiveGridRows
{
    private object? _held;
    private object? _scope;

    public Task ObserveAsync<TItem>(OmniDataGrid<TItem>? grid, object? rows, object? scope = null)
    {
        if (rows is null || ReferenceEquals(_held, rows)) return Task.CompletedTask;

        var initial = _held is null || !Equals(_scope, scope);
        _held = rows;
        _scope = scope;
        return initial || grid is null ? Task.CompletedTask : grid.RefreshAsync();
    }
}
