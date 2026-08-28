// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public sealed class AiRunnerProfile
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Binary { get; set; } = string.Empty;
    public string ArgsTemplateJson { get; set; } = "[]";
    public string EnvironmentJsonEncrypted { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxOutputBytes { get; set; } = 200_000;
    public bool SendsDataExternally { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<AiTaskDefinition> TaskDefinitions { get; set; } = [];
}
