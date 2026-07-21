// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Certbot;

[ApiController]
[Route("api/servers/{serverId:int}/certbot")]
[Authorize]
[ServiceFilter(typeof(ValidateServerExistsFilter))]
public class CertbotController(ICertbotService certbotService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CertbotCertificateDto>>> GetCertificates(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var certs = await certbotService.GetCertificatesAsync(serverId, ct);
        return Ok(certs);
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction(int serverId, [FromBody] CertbotActionRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await certbotService.ExecuteActionAsync(serverId, request, ct);
        return Ok();
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateCertificate(int serverId, [FromBody] CertbotCreateRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Admin, ct))
            return Forbid();
        await certbotService.CreateCertificateAsync(serverId, request, ct);
        return Ok();
    }
}
