// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.ServerConfigurations;

[ApiController]
[Route("api/servers/{serverId:int}/configuration")]
[Authorize]
public class ServerConfigurationController(
    IServerConfigurationService configService,
    IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("export")]
    public async Task<ActionResult<string>> ExportConfiguration(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();

        var yaml = await configService.ExportConfigurationAsync(serverId, ct);
        if (yaml is null) return NotFound();
        return Content(yaml, "application/x-yaml");
    }

    [HttpPost("validate")]
    public async Task<ActionResult<ServerConfigValidationResult>> ValidateConfiguration(
        int serverId,
        [FromBody] ServerConfigImportRequest request,
        CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();

        return Ok(await configService.ValidateConfigurationAsync(request.Yaml, ct));
    }

    [HttpPost("preview")]
    public async Task<ActionResult<ServerConfigPreviewDto>> PreviewImport(
        int serverId, [FromBody] ServerConfigImportRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();

        var result = await configService.PreviewImportAsync(serverId, request.Yaml, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("deploy")]
    public async Task<ActionResult<ServerConfigDeployResultDto>> DeployConfiguration(
        int serverId, [FromBody] ServerConfigImportRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Admin, ct))
            return Forbid();

        var result = await configService.DeployConfigurationAsync(serverId, request.Yaml, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }
}
