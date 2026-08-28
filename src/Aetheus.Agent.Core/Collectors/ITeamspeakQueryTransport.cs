// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Collectors;

public interface ITeamspeakQueryTransport
{
    Task<string> ExchangeAsync(int port, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
