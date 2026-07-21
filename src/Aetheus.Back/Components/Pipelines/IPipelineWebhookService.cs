// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineWebhookService
{
    Task<bool> HandleWebhookAsync(string rawBody, string? signature, CancellationToken ct = default);
}
