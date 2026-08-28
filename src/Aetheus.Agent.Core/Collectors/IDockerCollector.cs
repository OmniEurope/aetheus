// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IDockerCollector
{
    Task<DockerDataDto> CollectAllAsync(CancellationToken ct = default);
}
