// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Components.Organizations;
namespace Aetheus.Back.Components.Organizations;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class OrganizationsController(IOrganizationService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaginatedResult<OrganizationDto>>> GetOrganizations(
        [FromQuery] string? search,
        [FromQuery] PaginationRequest request,
        CancellationToken ct) =>
        Ok(await service.GetOrganizationsAsync(search, request, ct));

    /// <summary>
    /// Returns the organizations the current authenticated user is a member of.
    /// Open to any authenticated user (drives the header organization picker) - inherits only the
    /// class-level [Authorize], must NOT carry an Admin role requirement.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<List<MyOrganizationDto>>> GetMyOrganizations(CancellationToken ct)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Unauthorized();
        return Ok(await service.GetMyOrganizationsAsync(username, ct));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationDetailDto>> GetOrganization(int id, CancellationToken ct)
    {
        var org = await service.GetOrganizationAsync(id, ct);
        return org is null ? NotFound() : Ok(org);
    }

    /// <summary>Organizations a specific user is a member of (drives the user detail Organizations tab).</summary>
    [HttpGet("~/api/users/{userId:int}/organizations")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<UserOrganizationDto>>> GetUserOrganizations(int userId, CancellationToken ct) =>
        Ok(await service.GetOrganizationsForUserAsync(userId, ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization([FromBody] CreateOrganizationRequest request, CancellationToken ct)
    {
        var org = await service.CreateOrganizationAsync(request, ct);
        return CreatedAtAction(nameof(GetOrganization), new { id = org.Id }, org);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationDto>> UpdateOrganization(int id, [FromBody] UpdateOrganizationRequest request, CancellationToken ct)
    {
        var org = await service.UpdateOrganizationAsync(id, request, ct);
        return org is null ? NotFound() : Ok(org);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteOrganization(int id, CancellationToken ct)
    {
        var deleted = await service.DeleteOrganizationAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/members")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(int id, [FromBody] AddOrganizationMemberRequest request, CancellationToken ct) =>
        Ok(await service.AddMemberAsync(id, request, ct));

    [HttpPut("{id:int}/members/{memberId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationMemberDto>> UpdateMember(int id, int memberId, [FromBody] UpdateOrganizationMemberRequest request, CancellationToken ct)
    {
        var m = await service.UpdateMemberAsync(id, memberId, request, ct);
        return m is null ? NotFound() : Ok(m);
    }

    [HttpDelete("{id:int}/members/{memberId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveMember(int id, int memberId, CancellationToken ct)
    {
        var removed = await service.RemoveMemberAsync(id, memberId, ct);
        return removed ? NoContent() : NotFound();
    }

    [HttpPut("{id:int}/projects")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignProjects(int id, [FromBody] AssignProjectsRequest request, CancellationToken ct)
    {
        await service.AssignProjectsAsync(id, request.ProjectIds, ct);
        return NoContent();
    }
}
