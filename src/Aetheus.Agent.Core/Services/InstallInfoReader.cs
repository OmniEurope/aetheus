// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Resolves the "agent was last (re)installed" timestamp that the install script writes
/// to <c>$WORK_DIR/.installed-at</c>. The file holds a single ISO-8601 UTC line written by
/// <c>install-agent-linux.sh</c> / <c>install-agent-windows.ps1</c> in both fresh-install and
/// <c>--upgrade</c> paths; reading it once at heartbeat time gives the operator a reliable
/// "last update" indicator on the agent page.
///
/// Falls back to the agent assembly's last-write mtime so previously deployed agents (where
/// the install script ran before this marker existed) still report a meaningful value. Both
/// branches return UTC; never throws - a corrupt file or missing path quietly yields null.
/// </summary>
public static class InstallInfoReader
{
    public const string MarkerFileName = ".installed-at";

    public static DateTime? Read(string? workDirectory)
    {
        var fromMarker = TryReadMarker(workDirectory);
        if (fromMarker is not null) return fromMarker;

        return TryReadAssemblyMtime();
    }

    private static DateTime? TryReadMarker(string? workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory)) return null;

        var path = Path.Combine(workDirectory, MarkerFileName);
        // File.Exists swallows path-too-long / permission errors and returns false - exactly
        // the silent-skip behaviour we want; the assembly-mtime fallback covers the gap.
        if (!File.Exists(path)) return null;

        try
        {
            var content = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(content)) return null;

            // AssumeUniversal + AdjustToUniversal - the install scripts always emit a `Z` suffix,
            // but parse defensively so a hand-edited local-tz timestamp still lands on UTC.
            if (DateTime.TryParse(
                    content,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }
        catch (IOException) { /* unreadable - fall back to mtime */ }
        catch (UnauthorizedAccessException) { /* same */ }

        return null;
    }

    private static DateTime? TryReadAssemblyMtime()
    {
        try
        {
            var path = typeof(InstallInfoReader).Assembly.Location;
            // .NET single-file publish leaves Assembly.Location empty; in that case the only
            // meaningful answer is "unknown" - better than reporting a misleading time.
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
