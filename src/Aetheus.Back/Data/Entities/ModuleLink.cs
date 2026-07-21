// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class ModuleLink
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public Shared.Enums.ModuleLinkType SourceType { get; set; }
    public string SourceIdentifier { get; set; } = string.Empty;
    public Shared.Enums.ModuleLinkType TargetType { get; set; }
    public string TargetIdentifier { get; set; } = string.Empty;
    public bool IsAutoDetected { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
