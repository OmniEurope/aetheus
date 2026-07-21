// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class PortsentryState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public bool IsRunning { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string TcpPorts { get; set; } = string.Empty;
    public string UdpPorts { get; set; } = string.Empty;
    public int BlockedCount { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
