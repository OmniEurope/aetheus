// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Constants;

/// <summary>Env-var contract + bounds for the firewall operations (PLAN-006 4.2).</summary>
public static class FirewallConstants
{
    /// <summary>Protocol for an allow/deny rule (<c>tcp</c> or <c>udp</c>).</summary>
    public const string ProtoEnvVar = "AETHEUS_FIREWALL_PROTO";

    /// <summary>Source for an allow/deny rule: a CIDR (e.g. <c>10.0.0.0/8</c>) or <c>any</c>.</summary>
    public const string SourceEnvVar = "AETHEUS_FIREWALL_SOURCE";

    /// <summary>The detected admin SSH port (agent -&gt; helper), auto-allowed before <c>ufw enable</c>.</summary>
    public const string AdminPortEnvVar = "AETHEUS_FIREWALL_ADMIN_PORT";

    public static readonly IReadOnlySet<string> AllowedProtocols =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tcp", "udp" };
}

/// <summary>
/// Anti-lockout LOGIC guard (PLAN-006 4.2): the firewall must never lock the operator out of SSH. A deny
/// (or delete of an allow) on the administration port is refused; the admin port is auto-allowed before
/// the firewall is enabled. Pure + shared so the backend AND the agent enforce the same rule.
/// </summary>
public static class FirewallLockoutGuard
{
    /// <summary>Admin ports that must never be closed. 22 = SSH default; the agent extends this with the
    /// server's actually-configured SSH port before enforcing.</summary>
    public static readonly IReadOnlySet<int> DefaultAdminPorts = new HashSet<int> { 22 };

    /// <summary>True when the operation would (or could) close the administration port and lock the operator out.</summary>
    public static bool IsLockoutRisk(OperationKind operation, int port, IReadOnlySet<int>? adminPorts = null)
    {
        var ports = adminPorts ?? DefaultAdminPorts;
        if (!ports.Contains(port)) return false;
        return operation is OperationKind.FirewallDeny or OperationKind.FirewallDeleteRule;
    }
}
