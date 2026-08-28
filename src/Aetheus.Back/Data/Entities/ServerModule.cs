// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class ServerModule
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ServerModuleType Type { get; set; }
    public ServerModuleStatus Status { get; set; } = ServerModuleStatus.Unknown;
    public string? Version { get; set; }
    public string Configuration { get; set; } = "{}";
    public DateTime InstalledAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Server Server { get; set; } = null!;
}
