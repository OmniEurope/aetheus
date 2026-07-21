// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Helpers;

/// <summary>
/// Maps a free-form OS description (coming from the agent's RuntimeInformation.OSDescription
/// or equivalent) to a Material Icon name and a short OS family label. Used by the Servers
/// list to show a recognizable icon next to the full OS string.
/// </summary>
public static class OsIconHelper
{
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

        if (os.Contains("windows") || os.Contains("microsoft") || os.Contains("win32") || os.Contains("win64"))
            return OsFamily.Windows;

        if (os.Contains("darwin") || os.Contains("mac os") || os.Contains("macos") || os.Contains("osx"))
            return OsFamily.MacOs;

        if (os.Contains("linux") || os.Contains("ubuntu") || os.Contains("debian") ||
            os.Contains("centos") || os.Contains("fedora") || os.Contains("rhel") ||
            os.Contains("alpine") || os.Contains("arch") || os.Contains("suse") ||
            os.Contains("unix"))
            return OsFamily.Linux;

        return OsFamily.Unknown;
    }

    private enum OsFamily
    {
        Unknown,
        Linux,
        Windows,
        MacOs
    }
}
