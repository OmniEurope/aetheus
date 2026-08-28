// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class Environment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public EnvironmentType Type { get; set; } = EnvironmentType.Development;
    public int? ProjectId { get; set; }
    public bool RequireApproval { get; set; }
    public int ApprovalTimeoutMinutes { get; set; } = 1440;
    public string? ApprovalInstructions { get; set; }
    public bool DastEnabled { get; set; }
    public bool DastIsEphemeral { get; set; }
    public bool DastContainsRealData { get; set; }
    public string DastAllowedHosts { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public List<EnvironmentServer> Servers { get; set; } = [];
    public List<PipelineApproval> Approvals { get; set; } = [];
    public List<EnvironmentCheck> Checks { get; set; } = [];

    // Restructuration domaine projet : un environnement possède ses propres pipelines /
    // bibliothèques / vaults, et peut être lié à des serveurs-projet (informatifs).
    public List<Pipeline> Pipelines { get; set; } = [];
    public List<VariableLibrary> Libraries { get; set; } = [];
    public List<Vault> Vaults { get; set; } = [];
    public List<EnvironmentProjectServer> LinkedProjectServers { get; set; } = [];
}
