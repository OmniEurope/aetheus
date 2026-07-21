// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class ServerApp
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Type { get; set; }
    public ServerAppStatus Status { get; set; } = ServerAppStatus.Unknown;
    public int? Port { get; set; }
    public string? Path { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Server Server { get; set; } = null!;
}
