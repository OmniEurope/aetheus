// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Monitoring;

/// <summary>
/// S-TECH-RB4N: single source of truth for the product name and the derived string prefixes that a
/// rebrand would otherwise have to sweep across hundreds of literals (the Prometheus -> Aetheus rename
/// touched ~2440 files). Front localStorage keys, backend <c>/metrics</c> series and any other
/// brand-prefixed identifier should derive from here so the next rename is a one-line change.
/// </summary>
public static class AppConstants
{
    /// <summary>Human-facing product name.</summary>
    public const string ProductName = "Aetheus";

    /// <summary>Lowercase machine slug used to prefix identifiers.</summary>
    public const string ProductSlug = "aetheus";

    /// <summary>Prefix for browser localStorage keys (e.g. <c>aetheus_theme</c>).</summary>
    public const string LocalStoragePrefix = ProductSlug + "_";

    /// <summary>Prefix for <c>/metrics</c> OpenMetrics series names (e.g. <c>aetheus_backgroundqueue_length</c>).</summary>
    public const string MetricsPrefix = ProductSlug + "_";
}
