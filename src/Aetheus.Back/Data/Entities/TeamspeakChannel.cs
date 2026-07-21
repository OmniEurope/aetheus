// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class TeamspeakChannel
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public int ChannelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ParentId { get; set; }
    public int Order { get; set; }
    public int TotalClients { get; set; }
    public int MaxClients { get; set; } = -1;
    public bool IsDefault { get; set; }
    public bool HasPassword { get; set; }
    public bool IsPermanent { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
