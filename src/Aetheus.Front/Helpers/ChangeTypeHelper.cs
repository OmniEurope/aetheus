// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Helpers;

public static class ChangeTypeHelper
{
    public static BadgeStyle GetBadgeStyle(ChangeType type) => type switch
    {
        ChangeType.Created => BadgeStyle.Success,
        ChangeType.Updated => BadgeStyle.Info,
        ChangeType.Deleted => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };
}
