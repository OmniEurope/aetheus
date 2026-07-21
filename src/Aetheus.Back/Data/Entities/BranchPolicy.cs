// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class BranchPolicy
{
    public int Id { get; set; }
    public int GitConnectionId { get; set; }
    public string BranchPattern { get; set; } = string.Empty;
    public BranchPolicyType PolicyType { get; set; }
    public string? ConfigurationJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public GitConnection GitConnection { get; set; } = null!;
}
