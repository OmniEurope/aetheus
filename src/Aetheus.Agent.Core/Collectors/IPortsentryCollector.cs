// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IPortsentryCollector
{
    Task<PortsentryDataDto> CollectAsync(CancellationToken ct = default);
}
