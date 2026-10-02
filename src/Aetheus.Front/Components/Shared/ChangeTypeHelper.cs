// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public static class ChangeTypeHelper
{
    public static OmniTone GetBadgeStyle(ChangeType type) => type switch
    {
        ChangeType.Created => OmniTone.Success,
        ChangeType.Updated => OmniTone.Accent,
        ChangeType.Deleted => OmniTone.Danger,
        _ => OmniTone.Neutral
    };
}
