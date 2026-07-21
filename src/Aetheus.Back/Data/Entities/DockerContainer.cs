// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class DockerContainer
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Ports { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double MemoryUsageMb { get; set; }
    public double MemoryLimitMb { get; set; }
    public DateTime Created { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
