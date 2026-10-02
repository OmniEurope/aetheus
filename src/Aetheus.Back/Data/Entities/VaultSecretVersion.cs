// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class VaultSecretVersion
{
    public int Id { get; set; }
    public int VaultSecretId { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Recette R-287, expand step: the history keeps no secret value any more. New versions write an
    /// empty string and the migration blanked the stored ones; the column itself is dropped by the
    /// contract step, once no deployed binary maps it (listed in docs/contracts/deployment.md, pending contract steps).
    /// </summary>
    public string EncryptedValue { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime ChangedAt { get; set; }
    public ChangeType ChangeType { get; set; }

    /// <summary>Recette R-287: who made the change. Null on versions written before it was recorded.</summary>
    public string? ChangedBy { get; set; }

    // Navigation
    public VaultSecret? VaultSecret { get; set; }
}
