// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Shared.Validation;

/// <summary>
/// Shared validation for the typed Portsentry setup operation (mode + TCP/UDP port lists). Used by the
/// backend <c>OperationTargetValidator</c> (mode = task target) and re-checked agent-side by
/// <c>PortsentryOperationExecutor</c> before the root-owned portsentry-setup helper runs. Mirrors the
/// shapes the old <c>PortsentryCommandHelper</c> enforced.
/// </summary>
public static partial class PortsentryValidation
{
    /// <summary>Scan mode: alphanumeric only (e.g. "atcp", "tcp", "audp"). No metacharacters.</summary>
    public static bool IsValidMode(string? mode) =>
        !string.IsNullOrWhiteSpace(mode) && ModeRegex().IsMatch(mode);

    /// <summary>A comma-separated list of TCP/UDP ports in the inclusive 1-65535 range.</summary>
    public static bool IsValidPortList(string? ports)
    {
        if (string.IsNullOrWhiteSpace(ports) || !PortListRegex().IsMatch(ports)) return false;
        return ports.Split(',').All(port =>
            int.TryParse(port, out var value) && value is >= 1 and <= 65535);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9]{1,16}$")]
    private static partial Regex ModeRegex();

    [GeneratedRegex(@"^\d{1,5}(,\d{1,5})*$")]
    private static partial Regex PortListRegex();
}
