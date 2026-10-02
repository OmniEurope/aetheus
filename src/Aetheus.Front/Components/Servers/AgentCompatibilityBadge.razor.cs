// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Servers;

public partial class AgentCompatibilityBadge
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public AgentCompatibilityDto? Value { get; set; }

    private static OmniTone BadgeVariantFor(AgentCompatibilityStatus status) => status switch
    {
        AgentCompatibilityStatus.UpToDate => OmniTone.Success,
        AgentCompatibilityStatus.UpdateRecommended => OmniTone.Warning,
        AgentCompatibilityStatus.UpdateRequired => OmniTone.Danger,
        _ => OmniTone.Neutral
    };
}
