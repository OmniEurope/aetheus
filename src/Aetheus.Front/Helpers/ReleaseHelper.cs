// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Helpers;

internal static class ReleaseHelper
{
    internal static BadgeStyle GetReleaseBadge(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Detected => BadgeStyle.Info,
        ReleaseStatus.Building => BadgeStyle.Warning,
        ReleaseStatus.Published => BadgeStyle.Success,
        ReleaseStatus.Failed => BadgeStyle.Danger,
        ReleaseStatus.RolledBack => BadgeStyle.Secondary,
        ReleaseStatus.Promoted => BadgeStyle.Primary,
        _ => BadgeStyle.Light
    };
}
