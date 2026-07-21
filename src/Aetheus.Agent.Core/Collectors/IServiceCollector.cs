// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Collectors;

public interface IServiceCollector
{
    Task<List<ServiceInfoDto>> CollectAsync(CancellationToken ct = default);
}
