// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// Env-var keys carrying the <see cref="Aetheus.Shared.Enums.OperationKind.PortsentrySetup"/> port
/// lists from the backend to the agent. The scan mode travels in the task target; the TCP/UDP port lists
/// ride in these keys and are re-validated agent-side before the root-owned portsentry-setup helper runs.
/// Shared so both sides agree on the names.
/// </summary>
public static class PortsentrySetupEnv
{
    public const string TcpPorts = "AETHEUS_PORTSENTRY_TCP_PORTS";
    public const string UdpPorts = "AETHEUS_PORTSENTRY_UDP_PORTS";
}
