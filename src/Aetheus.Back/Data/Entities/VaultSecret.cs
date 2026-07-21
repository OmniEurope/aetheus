// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class VaultSecret
{
    public int Id { get; set; }
    public int VaultId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public Vault Vault { get; set; } = null!;
    public List<VaultSecretVersion> Versions { get; set; } = [];
}
