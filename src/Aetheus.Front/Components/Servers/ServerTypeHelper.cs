// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers;

public static class ServerTypeHelper
{
    public static string GetIcon(ServerType type) => type switch
    {
        ServerType.Normal => "dns",
        ServerType.Build => "build",
        ServerType.Docker => "inventory_2",
        _ => "dns"
    };

    public static OmniTone GetBadgeStyle(ServerType type) => type switch
    {
        ServerType.Normal => OmniTone.Accent,
        ServerType.Build => OmniTone.Warning,
        ServerType.Docker => OmniTone.Neutral,
        _ => OmniTone.Neutral
    };
}
