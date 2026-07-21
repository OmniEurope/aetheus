// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class Pipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string YamlDefinition { get; set; } = string.Empty;
    public PipelineTriggerType TriggerType { get; set; } = PipelineTriggerType.Manual;

    // Exactly-one-owner: exactly one of ProjectId / EnvironmentId / ProjectServerId is set
    // (legacy rows may have all null until re-saved). Enforced in the service layer.
    public int? ProjectId { get; set; }
    [MaxLength(255)]
    public string? SourceBranch { get; set; }
    public int? EnvironmentId { get; set; }
    public int? ProjectServerId { get; set; }

    // F-EXEC-1b: the user who created (or last re-saved an unowned) pipeline. Non-interactive
    // triggers (webhook / scheduler) have no ClaimsPrincipal, so automated runs are authorized
    // against this owner's permissions. Nullable: legacy rows have no owner and their automated
    // runs are denied fail-closed until an authorized user re-saves the pipeline.
    [MaxLength(256)]
    public string? CreatedByUsername { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    // Navigation
    public Project? Project { get; set; }
    public Environment? Environment { get; set; }
    public ProjectServer? ProjectServer { get; set; }
    public List<PipelineRun> Runs { get; set; } = [];
}
