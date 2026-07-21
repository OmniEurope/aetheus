// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

internal sealed class AgentProcessRestarter : IAgentProcessRestarter
{
    public void Restart(string reason)
    {
        Console.Error.WriteLine(reason);
        Environment.Exit(1);
    }
}
