// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Webhooks;

[ApiController]
[Route("api/webhooks")]
[Authorize(Roles = "Admin")]
public class WebhooksController(IWebhookService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WebhookSubscriptionDto>>> GetSubscriptions(CancellationToken ct)
    {
        return Ok(await service.GetSubscriptionsAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WebhookSubscriptionDto>> GetSubscription(int id, CancellationToken ct)
    {
        var result = await service.GetSubscriptionAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WebhookSubscriptionDto>> CreateSubscription(
        [FromBody] CreateWebhookSubscriptionRequest request, CancellationToken ct)
    {
        var result = await service.CreateSubscriptionAsync(request, ct);
        return CreatedAtAction(nameof(GetSubscription), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WebhookSubscriptionDto>> UpdateSubscription(
        int id, [FromBody] UpdateWebhookSubscriptionRequest request, CancellationToken ct)
    {
        var result = await service.UpdateSubscriptionAsync(id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSubscription(int id, CancellationToken ct)
    {
        return await service.DeleteSubscriptionAsync(id, ct) ? NoContent() : NotFound();
    }
}
