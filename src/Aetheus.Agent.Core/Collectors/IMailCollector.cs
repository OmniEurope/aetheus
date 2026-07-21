// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Collectors;

public interface IMailCollector
{
    Task<MailDataDto> CollectAsync(CancellationToken ct = default);
}
