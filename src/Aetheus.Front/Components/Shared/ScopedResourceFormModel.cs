// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public abstract class ScopedResourceFormModel
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public int? ProjectId { get; set; }
    public int? EnvironmentId { get; set; }
    public int? ProjectServerId { get; set; }
}
