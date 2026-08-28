// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface ISecurityUpdatesCollector
{
    Task<SecurityUpdatesDataDto> CollectAsync(CancellationToken ct = default);
}
