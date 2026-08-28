// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Toolchains;

public interface IToolchainResolver
{
    Task<ToolchainResolution> ResolveAsync(
        ContainerSpec spec,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
