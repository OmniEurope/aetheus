// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IApacheCollector
{
    Task<ApacheDataDto> CollectAsync(CancellationToken ct = default);
}
