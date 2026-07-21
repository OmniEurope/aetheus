// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class ApacheModule
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "static" or "shared"
    public bool IsEnabled { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
