// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class TeamspeakBan
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public int BanId { get; set; }
    public string Ip { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long Duration { get; set; }
    public long Created { get; set; }

    public Server Server { get; set; } = null!;
}
