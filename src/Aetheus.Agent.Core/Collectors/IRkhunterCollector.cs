// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IRkhunterCollector
{
    Task<RkhunterDataDto> CollectAsync(CancellationToken ct = default);
}
