// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers;

public partial class AgentCompatibilityBadge
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public AgentCompatibilityDto? Value { get; set; }

    private static BadgeStyle BadgeStyleFor(AgentCompatibilityStatus status) => status switch
    {
        AgentCompatibilityStatus.UpToDate => BadgeStyle.Success,
        AgentCompatibilityStatus.UpdateRecommended => BadgeStyle.Warning,
        AgentCompatibilityStatus.UpdateRequired => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };
}
