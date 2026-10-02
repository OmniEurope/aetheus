// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Standardized "Are you sure you want to delete X?" prompt (STD-BTN: red "Supprimer" confirm like the button that opened it, grey "Revenir") (F-26 / shared
/// component pass). Since PLAN-008 lot 11 the question is an OmniEurope.Blazor confirmation, asked
/// through <see cref="AppDialogs.ConfirmAsync"/>: the answer is true when the action is chosen and
/// false otherwise, never null (a dismissed dialog must not read as a yes).
/// </summary>
public sealed class ConfirmHelper(AppDialogs dialogs, IStringLocalizer<AppStrings> localizer)
{
    public async Task<bool?> ConfirmDeleteAsync(string messageKey, string? titleKey = null, params object[] args) =>
        await dialogs.ConfirmAsync(
            localizer[titleKey ?? "ConfirmDelete"].Value,
            Format(messageKey, args),
            localizer["Delete"].Value,
            localizer["GoBack"].Value,
            destructive: true);

    /// <summary>
    /// An ordinary confirmation whose blue button says the verb of <paramref name="confirmKey"/>
    /// (recette R-407: a short precise verb, never a generic "Valider").
    /// </summary>
    public async Task<bool?> ConfirmAsync(string confirmKey, string messageKey, string? titleKey = null, params object[] args) =>
        await dialogs.ConfirmAsync(
            localizer[titleKey ?? "Confirm"].Value,
            Format(messageKey, args),
            localizer[confirmKey].Value,
            localizer["GoBack"].Value);

    private string Format(string messageKey, object[] args) =>
        args.Length > 0
            ? string.Format(localizer[messageKey].Value, args)
            : localizer[messageKey].Value;
}
