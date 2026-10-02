// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.ServerModules;

public sealed record ServerModuleDto
{
    public int Id { get; init; }
    public int ServerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ServerModuleType Type { get; init; }
    public ServerModuleStatus Status { get; init; }
    public string? Version { get; init; }
    public DateTime InstalledAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record CreateServerModuleRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    public ServerModuleType Type { get; init; }

    [StringLength(50)]
    public string? Version { get; init; }

    [StringLength(4000)]
    public string? Configuration { get; init; }
}

public sealed record UpdateServerModuleRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    public ServerModuleStatus Status { get; init; }

    [StringLength(50)]
    public string? Version { get; init; }

    [StringLength(4000)]
    public string? Configuration { get; init; }
}
