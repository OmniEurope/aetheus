// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.Shared.Components.Servers;

/// <summary>
/// Validates the bounded firewall-rule args (ADR-024 4.2): protocol against the allow-list and the
/// source as <c>any</c>, a single IP, or a CIDR. Shared so the backend, the agent executor, and (in
/// spirit) the root-owned helper enforce the same bounds - no free-form firewall rule ever reaches ufw.
/// </summary>
public static class FirewallValidation
{
    public static bool IsValidProtocol(string? protocol)
        => protocol is not null && FirewallConstants.AllowedProtocols.Contains(protocol);

    public static bool IsValidSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (string.Equals(source, "any", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(source, out _)) return true;
        return IPNetwork.TryParse(source, out _);
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;
}
