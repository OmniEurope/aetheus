// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Services;

/// <summary>
/// Standardized "Are you sure you want to delete X?" prompt with danger styling.
/// Consolidates the duplicated <see cref="DialogService.Confirm(string, string, ConfirmOptions?)"/> calls
/// scattered across delete buttons (F-26 / shared component pass).
/// </summary>
public sealed class ConfirmHelper(DialogService dialog, IStringLocalizer<AppStrings> localizer)
{
    public Task<bool?> ConfirmDeleteAsync(string messageKey, string? titleKey = null, params object[] args)
    {
        var message = args.Length > 0
            ? string.Format(localizer[messageKey].Value, args)
            : localizer[messageKey].Value;
        var title = localizer[titleKey ?? "ConfirmDelete"].Value;

        return dialog.Confirm(message, title, new ConfirmOptions
        {
            OkButtonText = localizer["Delete"].Value,
            CancelButtonText = localizer["Cancel"].Value
        });
    }

    public Task<bool?> ConfirmAsync(string messageKey, string? titleKey = null, params object[] args)
    {
        var message = args.Length > 0
            ? string.Format(localizer[messageKey].Value, args)
            : localizer[messageKey].Value;
        var title = localizer[titleKey ?? "Confirm"].Value;

        return dialog.Confirm(message, title, new ConfirmOptions
        {
            OkButtonText = localizer["OK"].Value,
            CancelButtonText = localizer["Cancel"].Value
        });
    }
}
