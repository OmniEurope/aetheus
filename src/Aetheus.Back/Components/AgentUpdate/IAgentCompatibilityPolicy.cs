// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AgentUpdate;

public interface IAgentCompatibilityPolicy
{
    AgentCompatibilityDto Evaluate(ServerDto server);
    bool CanExecute(ServerDto server, string requiredCapability);
}
