// SPDX-License-Identifier: EUPL-1.2
using Radzen;

namespace Aetheus.Front.Services;

/// <summary>
/// Reusable wrapper around the result-driven UI mutation pattern:
/// run an API action, then show a success or error toast based on
/// whether the action returned a non-null result / <c>true</c>.
/// </summary>
public sealed class UiActions(NotifyHelper toast)
{
    /// <summary>
    /// Runs <paramref name="action"/>; if it returns a non-null result, shows the success
    /// toast (and invokes <paramref name="onSuccess"/>). Otherwise shows the error toast.
    /// Returns <c>true</c> when the action succeeded.
    /// </summary>
    public async Task<bool> RunAsync<T>(
        Func<Task<T?>> action,
        string successKey,
        Func<T, Task>? onSuccess = null,
        string errorKey = "SaveFailed",
        string errorTitleKey = "Error",
        string successTitleKey = "Saved")
        where T : class
    {
        var result = await action().ConfigureAwait(false);
        if (result is not null)
        {
            if (onSuccess is not null)
                await onSuccess(result).ConfigureAwait(false);
            toast.Success(successTitleKey, successKey);
            return true;
        }
        toast.Error(errorTitleKey, errorKey);
        return false;
    }

    /// <summary>Variant for boolean-returning actions (e.g. delete).</summary>
    public async Task<bool> RunAsync(
        Func<Task<bool>> action,
        string successKey,
        Func<Task>? onSuccess = null,
        string errorKey = "SaveFailed",
        string errorTitleKey = "Error",
        string successTitleKey = "Saved")
    {
        var ok = await action().ConfigureAwait(false);
        if (ok)
        {
            if (onSuccess is not null)
                await onSuccess().ConfigureAwait(false);
            toast.Success(successTitleKey, successKey);
            return true;
        }
        toast.Error(errorTitleKey, errorKey);
        return false;
    }

    /// <summary>Variant for status-returning actions (the migrated <see cref="ApiStatus"/> endpoints).
    /// Adapts to the same success/failure toast flow as the boolean variant.</summary>
    public async Task<bool> RunAsync(
        Func<Task<ApiStatus>> action,
        string successKey,
        Func<Task>? onSuccess = null,
        string errorKey = "SaveFailed",
        string errorTitleKey = "Error",
        string successTitleKey = "Saved")
    {
        var status = await action().ConfigureAwait(false);
        if (status.Success)
        {
            if (onSuccess is not null)
                await onSuccess().ConfigureAwait(false);
            toast.Success(successTitleKey, successKey);
            return true;
        }
        toast.Error(errorTitleKey, errorKey);
        return false;
    }
}
