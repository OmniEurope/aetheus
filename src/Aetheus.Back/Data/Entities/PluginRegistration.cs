// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class PluginRegistration
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Author { get; set; }
    public PluginType Type { get; set; }
    public PluginStatus Status { get; set; } = PluginStatus.Registered;
    public string? EntryPoint { get; set; }
    public string? ConfigurationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
}
