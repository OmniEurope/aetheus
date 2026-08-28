// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public sealed class AiTaskTrigger
{
    public int Id { get; set; }
    public int AiTaskDefinitionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public AiTaskDefinition AiTaskDefinition { get; set; } = null!;
}
