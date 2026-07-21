// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Back.Data.Entities;

public class Vault
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Exactly-one-owner: exactly one of ProjectId / EnvironmentId / ProjectServerId is set.
    // Enforced in the service layer + an architecture guard test.
    public int? ProjectId { get; set; }
    public int? EnvironmentId { get; set; }
    public int? ProjectServerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    // Navigation
    public Project? Project { get; set; }
    public Environment? Environment { get; set; }
    public ProjectServer? ProjectServer { get; set; }
    public List<VaultSecret> Secrets { get; set; } = [];
}
