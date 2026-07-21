// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AgentPoolServer
{
    public int AgentPoolId { get; set; }
    public int ServerId { get; set; }

    // Navigation
    public AgentPool AgentPool { get; set; } = null!;
    public Server Server { get; set; } = null!;
}
