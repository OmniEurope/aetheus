// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public sealed class AiTaskDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProfileId { get; set; }
    public string PromptTemplate { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public int? ServerId { get; set; }
    public string? Schedule { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AiRunnerProfile Profile { get; set; } = null!;
    public Project? Project { get; set; }
    public Server? Server { get; set; }
    public List<AiTaskTrigger> Triggers { get; set; } = [];
    public List<AiRunResult> Results { get; set; } = [];
}
