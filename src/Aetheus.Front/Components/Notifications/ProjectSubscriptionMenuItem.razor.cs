// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Notifications;

public partial class ProjectSubscriptionMenuItem
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>Recette R-431: the follow state the project header loaded for its project.</summary>
    [Parameter, EditorRequired] public ProjectSubscriptionState State { get; set; } = default!;
}
