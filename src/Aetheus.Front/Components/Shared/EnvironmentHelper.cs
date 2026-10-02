// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

internal static class EnvironmentHelper
{
    /// <summary>Badge style for an environment type - red for production, amber for staging, blue for
    /// testing, neutral otherwise. Consolidates the badge mapping that was duplicated (and divergent)
    /// across the global page and the project section.</summary>
    internal static OmniTone TypeStyle(EnvironmentType type) => type switch
    {
        EnvironmentType.Production => OmniTone.Danger,
        EnvironmentType.Staging => OmniTone.Warning,
        EnvironmentType.Testing => OmniTone.Accent,
        _ => OmniTone.Success
    };
}
