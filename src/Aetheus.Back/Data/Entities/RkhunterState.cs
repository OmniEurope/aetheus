// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class RkhunterState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string DatabaseVersion { get; set; } = string.Empty;
    public DateTime LastScanTime { get; set; }
    public string LastScanStatus { get; set; } = string.Empty;
    public int WarningCount { get; set; }
    public DateTime DatabaseLastUpdated { get; set; }
    public string? ScanScheduleCron { get; set; }
    public DateTime? LastScheduledScanAt { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
