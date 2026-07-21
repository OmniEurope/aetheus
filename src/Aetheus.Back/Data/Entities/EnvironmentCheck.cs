// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class EnvironmentCheck
{
    public int Id { get; set; }
    public int EnvironmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EnvironmentCheckType Type { get; set; }
    public string? Configuration { get; set; }
    public bool IsRequired { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 300;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Environment Environment { get; set; } = null!;
}
