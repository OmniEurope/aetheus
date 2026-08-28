// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface ITeamspeakCollector
{
    Task<TeamspeakDataDto> CollectAsync(CancellationToken ct = default);
}
