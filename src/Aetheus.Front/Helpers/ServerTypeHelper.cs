// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Helpers;

public static class ServerTypeHelper
{
    public static string GetIcon(ServerType type) => type switch
    {
        ServerType.Normal => "dns",
        ServerType.Build => "build",
        ServerType.Docker => "inventory_2",
        _ => "dns"
    };

    public static BadgeStyle GetBadgeStyle(ServerType type) => type switch
    {
        ServerType.Normal => BadgeStyle.Info,
        ServerType.Build => BadgeStyle.Warning,
        ServerType.Docker => BadgeStyle.Secondary,
        _ => BadgeStyle.Light
    };
}
