// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Users;

public sealed class RoleFormModel
{
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string Description { get; set; } = string.Empty;
}
