// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// The setup wizard's own surface. It sits beside <see cref="PipelinesController"/> rather than
/// inside it because it answers a question about a project that has no pipelines yet, and because
/// that controller is already at its size budget.
/// </summary>
[ApiController]
[Route("api/pipelines/setup")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public class PipelineSetupController(
    IPipelineSetupReadinessService setupReadiness,
    IPipelineRequirementsProvisioner provisioner,
    IResourceAuthorizationService authz) : ControllerBase
{
    /// <summary>
    /// What would stop the pipelines the wizard is about to create from running. Gated on WRITE
    /// access to the project - the same permission the creation itself needs - so it cannot be used
    /// to probe the repository or fleet of a project the caller may not change.
    /// </summary>
    [HttpPost("readiness")]
    public async Task<ActionResult<PipelineSetupReadinessDto>> CheckReadiness(
        [FromBody] PipelineSetupReadinessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId, Permission.Write, ct))
            return Forbid();

        var organizationId = await ResolveOrganizationIdAsync(ct);

        return Ok(await setupReadiness.CheckAsync(
            request.ProjectId, request.TemplateNames, organizationId, ct));
    }

    /// <summary>
    /// PLAN-003 lot 30: the libraries and vaults this project's pipelines require and it lacks. Read
    /// access to the project suffices: it only names resources and pipelines of that project.
    /// </summary>
    [HttpGet("unmet")]
    public async Task<ActionResult<List<UnmetRequirementDto>>> GetUnmetRequirements(
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();

        var organizationId = await ResolveOrganizationIdAsync(ct);

        return Ok(await provisioner.GetUnmetAsync(projectId, organizationId, ct));
    }

    /// <summary>
    /// PLAN-003 lot 30 / D22: creates the libraries and vaults the selected templates require and the
    /// project lacks, with their keys and empty values. Needs write on the project and the right to
    /// create libraries and vaults; the key names are only read from resources the caller can already
    /// read, so the action discloses nothing the caller could not open themselves.
    /// </summary>
    [HttpPost("provision")]
    public async Task<ActionResult<PipelineRequirementsProvisionResultDto>> ProvisionRequirements(
        [FromBody] PipelineSetupReadinessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AuthorizeProvisioningAsync(request.ProjectId, ct) is not { } scope)
            return Forbid();

        return Ok(await provisioner.ProvisionAsync(
            request.ProjectId, request.TemplateNames, scope.OrganizationId, scope.ReadableLibraries, scope.ReadableVaults, ct));
    }

    /// <summary>
    /// PLAN-003 lot 30: the overview's one-click fix. Creates what <see cref="GetUnmetRequirements"/>
    /// reports for the project's existing pipelines, under the same permissions as the wizard's.
    /// </summary>
    [HttpPost("provision-unmet")]
    public async Task<ActionResult<PipelineRequirementsProvisionResultDto>> ProvisionUnmetRequirements(
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int projectId, CancellationToken ct)
    {
        if (await AuthorizeProvisioningAsync(projectId, ct) is not { } scope)
            return Forbid();

        return Ok(await provisioner.ProvisionUnmetAsync(
            projectId, scope.OrganizationId, scope.ReadableLibraries, scope.ReadableVaults, ct));
    }

    // An admin sees the whole fleet; anyone else is answered about their own organization, so a
    // runner they could never reach is not counted as one they have.
    private async Task<int?> ResolveOrganizationIdAsync(CancellationToken ct)
    {
        var organizationIds = await authz.GetUserOrganizationIdsAsync(User, ct);
        int? organizationId = User.IsInRole("Admin") ? null : organizationIds.FirstOrDefault();
        return organizationId is <= 0 ? null : organizationId;
    }

    // Provisioning writes to the project and creates libraries and vaults: all three rights are needed
    // (null = forbidden). Key names are then read only from the libraries and vaults the caller can read.
    private async Task<(int? OrganizationId, List<int>? ReadableLibraries, List<int>? ReadableVaults)?> AuthorizeProvisioningAsync(
        int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct)
            || !await authz.HasPermissionAsync(User, ResourceType.VariableLibrary, null, Permission.Write, ct)
            || !await authz.HasPermissionAsync(User, ResourceType.Vault, null, Permission.Write, ct))
            return null;

        return (await ResolveOrganizationIdAsync(ct),
            await authz.GetAccessibleResourceIdsAsync(User, ResourceType.VariableLibrary, Permission.Read, ct),
            await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Vault, Permission.Read, ct));
    }
}
