// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IFirewallCollector
{
    Task<FirewallDataDto> CollectAsync(CancellationToken ct = default);
}
