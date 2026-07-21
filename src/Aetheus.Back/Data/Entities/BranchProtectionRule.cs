// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class BranchProtectionRule
{
    public int Id { get; set; }
    public int GitInternalRepoId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public bool PreventDeletion { get; set; } = true;
    public bool PreventForcePush { get; set; } = true;
    public bool RequirePullRequest { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public GitInternalRepo Repo { get; set; } = null!;
}
