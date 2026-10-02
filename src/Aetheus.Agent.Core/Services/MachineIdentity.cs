// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// PLAN-004 R-11: the host's stable machine identity, reported at enrollment so the backend can
/// recognise a reinstalled machine and revive its retired server instead of enrolling a new one.
/// Sources: Linux <c>/etc/machine-id</c> (fallback <c>/var/lib/dbus/machine-id</c>), Windows
/// <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>. The raw value never leaves the host:
/// machine-id(5) asks applications not to expose it, and to key its hash with an application-specific
/// key, so the agent sends an HMAC-SHA256 under a fixed Aetheus key (lowercase hex, 64 chars).
/// </summary>
public static class MachineIdentity
{
    internal const string LinuxMachineIdPath = "/etc/machine-id";
    internal const string LinuxDbusMachineIdPath = "/var/lib/dbus/machine-id";

    // Fixed forever: changing it changes every host's hash, and no reinstalled machine would be
    // recognised any more.
    private static readonly byte[] ApplicationKey = Encoding.UTF8.GetBytes("Aetheus.MachineIdentity.v1");

    /// <summary>Reads this host's identity and returns its hash, or null when the host exposes none.</summary>
    public static string? ReadHash() => OperatingSystem.IsWindows()
        ? ComputeHash(isWindows: true, ReadFileOrNull, ReadWindowsMachineGuid)
        : ComputeHash(isWindows: false, ReadFileOrNull, static () => null);

    /// <summary>Pure core of <see cref="ReadHash"/>, with the file and registry readers injected.</summary>
    internal static string? ComputeHash(bool isWindows, Func<string, string?> readFile, Func<string?> readMachineGuid)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(readMachineGuid);
        var raw = isWindows
            ? readMachineGuid()
            : FirstNonBlank(readFile(LinuxMachineIdPath), () => readFile(LinuxDbusMachineIdPath));
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var normalized = raw.Trim().ToLowerInvariant();
        var hash = HMACSHA256.HashData(ApplicationKey, Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }

    private static string? FirstNonBlank(string? first, Func<string?> fallback) =>
        string.IsNullOrWhiteSpace(first) ? fallback() : first;

    private static string? ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        try
        {
            // 64-bit view explicitly: a 32-bit view is redirected to WOW6432Node, which has no MachineGuid.
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
