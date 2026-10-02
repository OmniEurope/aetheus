// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Plugins;

// --- T-02: Extension / Plugin System ---

public sealed record PluginRegistrationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Version { get; init; } = string.Empty;
    public string? Author { get; init; }
    public PluginType Type { get; init; }
    public PluginStatus Status { get; init; }
    public string? EntryPoint { get; init; }
    public string? ConfigurationJson { get; init; }
    public int OrganizationId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record RegisterPluginRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty; // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    [StringLength(500)]
    public string? Description { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    [Required]
    [StringLength(50)]
    public string Version { get; set; } = string.Empty; // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    [StringLength(200)]
    public string? Author { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    public PluginType Type { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    [StringLength(500)]
    public string? EntryPoint { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    [StringLength(4000)]
    public string? ConfigurationJson { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)

    /// <summary>Organization the plugin belongs to. Optional -
    /// when omitted, the API uses the caller's default organization.</summary>
    [Range(1, int.MaxValue)]
    public int? OrganizationId { get; set; } // audit: kept set; (mutated post-construction by Components/Plugins/PluginManagement.razor @bind)
}

public sealed record UpdatePluginRequest
{
    [StringLength(500)]
    public string? Description { get; init; }

    public PluginStatus Status { get; init; }

    [StringLength(500)]
    public string? EntryPoint { get; init; }

    [StringLength(4000)]
    public string? ConfigurationJson { get; init; }
}
