// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Helpers;

/// <summary>
/// Maps a free-form OS description (coming from the agent's RuntimeInformation.OSDescription
/// or equivalent) to a Material Icon name and a short OS family label. Used by the Servers
/// list to show a recognizable icon next to the full OS string.
/// </summary>
public static class OsIconHelper
{
    private static readonly string[] WindowsTokens = ["windows", "microsoft", "win32", "win64"];
    private static readonly string[] MacTokens = ["darwin", "mac os", "macos", "osx"];
    private static readonly string[] LinuxTokens =
        ["linux", "ubuntu", "debian", "centos", "fedora", "rhel", "alpine", "arch", "suse", "unix"];

    public static string GetIcon(string? osDescription)
    {
        var family = GetFamily(osDescription);
        return family switch
        {
            OsFamily.Linux => "terminal",        // closest Material Icon to Tux
            OsFamily.Windows => "desktop_windows",
            OsFamily.MacOs => "laptop_mac",
            _ => "dns"
        };
    }

    public static string GetColorClass(string? osDescription)
    {
        var family = GetFamily(osDescription);
        return family switch
        {
            OsFamily.Linux => "server-os-linux",
            OsFamily.Windows => "server-os-windows",
            OsFamily.MacOs => "server-os-macos",
            _ => "server-os-unknown"
        };
    }

    public static string GetFamilyLabel(string? osDescription) => GetFamily(osDescription) switch
    {
        OsFamily.Linux => "Linux",
        OsFamily.Windows => "Windows",
        OsFamily.MacOs => "macOS",
        _ => "Unknown"
    };

    private static OsFamily GetFamily(string? osDescription)
    {
        if (string.IsNullOrWhiteSpace(osDescription))
            return OsFamily.Unknown;

        var os = osDescription.ToLowerInvariant();

        if (ContainsAny(os, WindowsTokens))
            return OsFamily.Windows;
        if (ContainsAny(os, MacTokens))
            return OsFamily.MacOs;
        if (ContainsAny(os, LinuxTokens))
            return OsFamily.Linux;
        return OsFamily.Unknown;
    }

    private static bool ContainsAny(string value, IEnumerable<string> tokens) =>
        tokens.Any(value.Contains);

    private enum OsFamily
    {
        Unknown,
        Linux,
        Windows,
        MacOs
    }
}
