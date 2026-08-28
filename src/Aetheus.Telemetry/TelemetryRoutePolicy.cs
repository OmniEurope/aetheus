// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Telemetry;

internal static class TelemetryRoutePolicy
{
    private static readonly string[] ExcludedPrefixes =
    [
        "/health",
        "/_framework",
        "/_content",
        "/favicon",
        "/robots.txt"
    ];

    public static bool IsExcludedPath(string? path) =>
        path is not null && ExcludedPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
