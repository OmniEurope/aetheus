// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface ICertbotCollector
{
    Task<CertbotDataDto> CollectAsync(CancellationToken ct = default);
}
