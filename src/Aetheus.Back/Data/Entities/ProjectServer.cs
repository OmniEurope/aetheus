// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class ProjectServer
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? ServerId { get; set; }
    public ProjectServerType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public Server? Server { get; set; }

    // Restructuration domaine projet : un serveur-projet (purement informatif) possède ses
    // propres pipelines / bibliothèques / vaults, et peut être lié à des environnements.
    public List<Pipeline> Pipelines { get; set; } = [];
    public List<VariableLibrary> Libraries { get; set; } = [];
    public List<Vault> Vaults { get; set; } = [];
    public List<EnvironmentProjectServer> LinkedEnvironments { get; set; } = [];
}
