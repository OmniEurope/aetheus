// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class ServerToken
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
