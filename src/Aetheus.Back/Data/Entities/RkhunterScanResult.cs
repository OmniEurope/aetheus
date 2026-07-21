// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class RkhunterScanResult
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public DateTime ScanTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public int WarningCount { get; set; }
    public string Summary { get; set; } = string.Empty;

    // Navigation
    public Server Server { get; set; } = null!;
}
