// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class PortsentryWhitelistIp
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
