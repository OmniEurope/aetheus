// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Navigation
    public List<UserRole> UserRoles { get; set; } = [];
    public List<ResourcePermission> ResourcePermissions { get; set; } = [];
}
