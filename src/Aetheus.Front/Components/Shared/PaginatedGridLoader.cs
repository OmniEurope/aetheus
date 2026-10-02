// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public static class PaginatedGridLoader
{
    /// <summary>Retries a page load through the shared OE grid wrapper.</summary>
    public static Task RetryAsync<T>(AetheusDataGrid<T>? grid)
        where T : notnull => grid?.Reload() ?? Task.CompletedTask;

    public static async Task LoadAsync<T>(
        Func<Task<PaginatedResult<T>>> loadAsync,
        Action<PaginatedResult<T>> applyResult,
        Action<bool> setLoading,
        Action<bool> setLoadFailed)
        where T : notnull
    {
        setLoading(true);
        setLoadFailed(false);
        try
        {
            applyResult(await loadAsync());
        }
        catch (HttpRequestException)
        {
            setLoadFailed(true);
        }
        finally
        {
            setLoading(false);
        }
    }
}
