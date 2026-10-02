// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Collectors;

/// <summary>PLAN-005 lot 2: which TCP ports are actually listening on this host, and who holds them.</summary>
public interface IListeningPortsCollector
{
    Task<List<ObservedPortDto>> CollectAsync(CancellationToken ct = default);
}
