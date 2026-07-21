// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class EnvironmentServer
{
    public int EnvironmentId { get; set; }
    public int ServerId { get; set; }

    // Navigation
    public Environment Environment { get; set; } = null!;
    public Server Server { get; set; } = null!;
}
