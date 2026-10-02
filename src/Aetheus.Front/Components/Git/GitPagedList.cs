// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Git;

/// <summary>
/// One server-paged list of the repository page (commits, pull requests): the rows its grid shows, their
/// total, and the loading and error states its tab renders. Recette R-327: the grid scrolls (remote
/// virtualization), so <see cref="Items"/> is the block the grid asked for last.
/// </summary>
/// <param name="isLoadFailure">The exceptions the tab reports as a failed load; any other one propagates.</param>
internal sealed class GitPagedList<T>(Func<Exception, bool> isLoadFailure)
{
    public List<T> Items { get; private set; } = [];
    public int TotalCount { get; private set; }
    public bool Loading { get; private set; }
    public bool Error { get; private set; }

    public void Clear()
    {
        Items = [];
        TotalCount = 0;
    }

    /// <summary>A page-level load (first load, retry): straight from the server, a failure empties the
    /// list and raises the error state.</summary>
    public async Task LoadAsync(Func<Task<PaginatedResult<T>>> fetch)
    {
        Loading = true;
        Error = false;
        try
        {
            Apply(await fetch());
        }
        catch (Exception ex) when (isLoadFailure(ex))
        {
            Clear();
            Error = true;
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>
    /// A block asked by the grid (S-TECH-SWGT): the cached copy paints at once, then the server revalidates
    /// it. A failure after a cache hit keeps the cached rows on screen and still raises the error state.
    /// </summary>
    public async Task LoadCachedAsync(ListCacheService cache, string key, Func<Task<PaginatedResult<T>>> fetch)
    {
        var hit = cache.TryGet<PaginatedResult<T>>(key, out var cached) && cached is not null;
        if (hit) Apply(cached!);
        Loading = !hit;
        Error = false;
        try
        {
            var result = await fetch();
            Apply(result);
            cache.Set(key, result);
        }
        catch (Exception ex) when (isLoadFailure(ex))
        {
            if (!hit) Clear();
            Error = true;
        }
        finally
        {
            Loading = false;
        }
    }

    private void Apply(PaginatedResult<T> result)
    {
        Items = result.Items;
        TotalCount = result.TotalCount;
    }
}
