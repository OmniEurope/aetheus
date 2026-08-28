// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// ADR-024 4.1: the default blocklist of system-critical packages a fleet <c>apt-get upgrade</c> must
/// not silently touch. A single source of truth shared by the backend (surfacing the policy) and the
/// agent (last-line defence: the <c>SystemPackageUpgrade</c> executor runs the dry-run first and ABORTS
/// honestly if any blocked package would be upgraded - never a fake success). Matching is by exact name
/// or by family prefix (kernel images, libc, openssl variants) so a versioned package like
/// <c>linux-image-6.8.0-51-generic</c> is caught.
/// <para>Rationale: upgrading these in an unattended window is the most likely way to break a running
/// app's ABI or the box itself (OpenSSL/libc), or to require a reboot (kernel). An operator upgrades
/// them deliberately, in a maintenance window, not via the fleet button.</para>
/// </summary>
public static class CriticalPackages
{
    /// <summary>Exact package names that must never be auto-upgraded.</summary>
    private static readonly IReadOnlySet<string> ExactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "libc6", "libc-bin", "openssl", "libssl3", "libssl1.1",
        "systemd", "systemd-sysv", "udev", "sudo", "openssh-server", "openssh-client",
        "grub-common", "grub-pc", "grub-efi-amd64",
        "docker.io", "docker-ce", "containerd", "postgresql", "mysql-server", "mariadb-server",
    };

    /// <summary>Package-name prefixes (family match) that must never be auto-upgraded.</summary>
    private static readonly string[] FamilyPrefixes =
    [
        "linux-image", "linux-headers", "linux-generic", "linux-modules",
        "libc6-", "libssl", "postgresql-", "mysql-", "mariadb-",
    ];

    /// <summary>True when <paramref name="package"/> is on the default critical blocklist (exact or family).</summary>
    public static bool IsCritical(string? package)
    {
        if (string.IsNullOrWhiteSpace(package)) return false;
        var name = package.Trim();
        if (ExactNames.Contains(name)) return true;
        foreach (var prefix in FamilyPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>The exact-name blocklist, for display/diagnostics.</summary>
    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)ExactNames;
}
