// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class ApacheState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public string ConfigRoot { get; set; } = string.Empty;

    /// <summary>True when the agent could not reliably enumerate vhosts/modules (privilege issue).</summary>
    public bool CollectionDegraded { get; set; }

    /// <summary>Short reason the collection degraded - surfaced in the UI for self-diagnosis.</summary>
    public string CollectionDiagnostics { get; set; } = string.Empty;

    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
