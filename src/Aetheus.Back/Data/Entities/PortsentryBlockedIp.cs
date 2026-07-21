// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class PortsentryBlockedIp
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; }
    public string Reason { get; set; } = string.Empty;

    // Navigation
    public Server Server { get; set; } = null!;
}
