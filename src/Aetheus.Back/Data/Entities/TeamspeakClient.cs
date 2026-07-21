// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class TeamspeakClient
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public int ClientId { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long IdleTimeSeconds { get; set; }
    public long ConnectionTimeSeconds { get; set; }
    public bool IsServerQuery { get; set; }

    public Server Server { get; set; } = null!;
}
