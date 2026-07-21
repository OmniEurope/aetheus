// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Helpers;

/// <summary>
/// Maps between the agent's free-text OS description, the structured <see cref="OsType"/>, and the
/// pipeline YAML <c>os:</c> token. Single source of truth so backend resolution and any UI agree.
/// </summary>
public static class OsTypeHelper
{
    /// <summary>Derive a structured <see cref="OsType"/> from the agent's free-text OS description.</summary>
    public static OsType FromDescription(string? osDescription)
    {
        if (string.IsNullOrWhiteSpace(osDescription)) return OsType.Unknown;
        if (osDescription.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return OsType.Windows;
        if (osDescription.Contains("Linux", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("Debian", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("CentOS", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("Fedora", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("Alpine", StringComparison.OrdinalIgnoreCase)
            || osDescription.Contains("Unix", StringComparison.OrdinalIgnoreCase))
            return OsType.Linux;
        return OsType.Unknown;
    }

    /// <summary>
    /// Parse a pipeline YAML <c>os:</c> token. Unset/empty ⇒ <see cref="OsType.Unknown"/> (no
    /// constraint). An unrecognized non-empty value also maps to Unknown; YAML validation surfaces
    /// it as a warning so a typo doesn't silently constrain to nothing.
    /// </summary>
    public static OsType Parse(string? os)
    {
        if (string.IsNullOrWhiteSpace(os)) return OsType.Unknown;
        return os.Trim().ToLowerInvariant() switch
        {
            "linux" or "ubuntu" or "unix" => OsType.Linux,
            "windows" or "win" => OsType.Windows,
            _ => OsType.Unknown
        };
    }

    /// <summary>True when <paramref name="os"/> is a non-empty token Parse does not recognize.</summary>
    public static bool IsUnrecognized(string? os)
        => !string.IsNullOrWhiteSpace(os) && Parse(os) == OsType.Unknown;

    /// <summary>
    /// Effective runner OS: the structured <see cref="OsType"/> when known, else derived from the
    /// free-text description (servers enrolled before OS typing self-heal on their next heartbeat,
    /// but may still be <see cref="OsType.Unknown"/> in between).
    /// </summary>
    public static OsType Effective(OsType osType, string? osDescription)
        => osType == OsType.Unknown ? FromDescription(osDescription) : osType;

    /// <summary>Single source of truth for "does this server run Windows" decisions (F-005).</summary>
    public static bool IsWindows(OsType osType, string? osDescription)
        => Effective(osType, osDescription) == OsType.Windows;
}
