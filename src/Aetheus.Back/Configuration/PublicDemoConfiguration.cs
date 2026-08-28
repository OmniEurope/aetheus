// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Configuration;

internal static class PublicDemoConfiguration
{
    internal static bool IsEnabled(IConfiguration? configuration)
    {
        if (configuration is null) return false;

        return configuration.GetValue("Aetheus:PublicDemo", false)
            && configuration.GetValue("Seed:Demo", false)
            && string.Equals(
                configuration["Aetheus:EnvironmentTier"],
                "qa",
                StringComparison.OrdinalIgnoreCase);
    }
}
