// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IServiceCollector
{
    Task<List<ServiceInfoDto>> CollectAsync(CancellationToken ct = default);
}
