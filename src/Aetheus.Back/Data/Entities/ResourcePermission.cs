// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class ResourcePermission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public ResourceType ResourceType { get; set; }
    public int? ResourceId { get; set; }
    public Permission Permission { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
}
