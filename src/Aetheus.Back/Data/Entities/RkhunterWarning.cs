// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class RkhunterWarning
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime FoundAt { get; set; }
    public bool IsArchived { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
