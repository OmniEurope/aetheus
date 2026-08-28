// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public static class PaginatedGridLoader
{
    public static Task RetryAsync<T>(RadzenDataGrid<T>? grid)
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
