// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Helpers;

internal static class EnvironmentHelper
{
    /// <summary>Badge style for an environment type - red for production, amber for staging, blue for
    /// testing, neutral otherwise. Consolidates the badge mapping that was duplicated (and divergent)
    /// across the global page and the project section.</summary>
    internal static BadgeStyle TypeStyle(EnvironmentType type) => type switch
    {
        EnvironmentType.Production => BadgeStyle.Danger,
        EnvironmentType.Staging => BadgeStyle.Warning,
        EnvironmentType.Testing => BadgeStyle.Info,
        _ => BadgeStyle.Success
    };
}
