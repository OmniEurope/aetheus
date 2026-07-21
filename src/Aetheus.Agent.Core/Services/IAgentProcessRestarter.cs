// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

public interface IAgentProcessRestarter
{
    void Restart(string reason);
}
