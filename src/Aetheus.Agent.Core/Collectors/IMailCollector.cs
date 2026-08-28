// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public interface IMailCollector
{
    Task<MailDataDto> CollectAsync(CancellationToken ct = default);
}
