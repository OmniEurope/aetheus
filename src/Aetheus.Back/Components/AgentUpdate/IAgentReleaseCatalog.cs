// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AgentUpdate;

public interface IAgentReleaseCatalog
{
    AgentReleaseManifestDto Current { get; }
}
