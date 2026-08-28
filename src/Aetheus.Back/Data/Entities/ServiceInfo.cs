// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class ServiceInfo
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ServiceType Type { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsManageable { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
