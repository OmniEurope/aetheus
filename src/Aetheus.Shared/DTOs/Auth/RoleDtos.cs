// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record RoleDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int PermissionCount { get; init; }
    public int UserCount { get; init; }
    public List<ResourcePermissionDto> Permissions { get; init; } = [];
}

public sealed record CreateRoleRequest
{
    [Required]
    [StringLength(50)]
    public string Name { get; init; } = string.Empty;

    [StringLength(200)]
    public string Description { get; init; } = string.Empty;
}

public sealed record UpdateRoleRequest
{
    [Required]
    [StringLength(50)]
    public string Name { get; init; } = string.Empty;

    [StringLength(200)]
    public string Description { get; init; } = string.Empty;
}

/// <summary>A user that holds a given role, for the role detail "Users" tab.</summary>
public sealed record RoleUserDto(int UserId, string Username, string? Email, bool IsActive);

/// <summary>Request to add an existing user to a role.</summary>
public sealed record AddUserToRoleRequest
{
    [Range(1, int.MaxValue)]
    public int UserId { get; init; }
}

public sealed record ResourcePermissionDto
{
    public int Id { get; init; }
    public ResourceType ResourceType { get; init; }
    public int? ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public Permission Permission { get; init; }
}

public sealed record SetResourcePermissionsRequest
{
    [Required]
    [MaxLength(200)]
    public List<ResourcePermissionEntry> Permissions { get; init; } = [];
}

public sealed record ResourcePermissionEntry
{
    public ResourceType ResourceType { get; init; }
    public int? ResourceId { get; init; }
    public Permission Permission { get; init; }
}

public sealed record UserPermissionSummaryDto
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = [];
    public List<EffectivePermissionDto> EffectivePermissions { get; init; } = [];
}

public sealed record EffectivePermissionDto
{
    public ResourceType ResourceType { get; init; }
    public int? ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public Permission Permission { get; init; }
    public string GrantedByRole { get; init; } = string.Empty;
}
