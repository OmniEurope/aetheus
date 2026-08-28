// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.SystemLogs;

[ApiController]
[Route("api/client-errors")]
[Authorize]
public sealed class ClientErrorsController(ILogger<ClientErrorsController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Report([FromBody] ClientErrorLogRequest request)
    {
        logger.LogError(
            "Client error at {ClientPath}: {Summary} - {ClientMessage}; correlation {CorrelationId}",
            request.Path ?? "(unknown)",
            request.Summary,
            request.Message,
            request.CorrelationId);
        return NoContent();
    }
}
